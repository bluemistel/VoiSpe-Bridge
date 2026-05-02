using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using SharpCompress.Common;
using SharpCompress.Readers;
using SherpaOnnx;

namespace VoiSpeBridge.App.Services;

/// <summary>
/// ReazonSpeech（日本語特化）+ SherpaOnnx オフライン音声認識サービス。
///
/// 使用モデル: sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01
///   - ReazonSpeech データセットで訓練された Zipformer transducer
///   - CPU 専用実行（GPU 不要）
///
/// フロー（Whisper と同じ VAD パターン）:
///   NAudio WaveInEvent (16kHz, 16bit, mono)
///     → RMS による VAD（無音区間で自動確定）
///     → SherpaOnnx OfflineRecognizer で推論（バックグラウンドスレッド）
///     → TextRecognized イベント発火
/// </summary>
public sealed class ReazonSpeechRecognitionService : IDisposable
{
    private const int SampleRate = 16000;

    // ---- モデルストア ----

    private static readonly string ModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiSpeBridge", "models", "reazonspeech");

    // GitHub releases から tar.bz2 を取得（認証不要・公開リリース）
    private const string ModelArchiveUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
        "sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01.tar.bz2";

    private const string ArchiveDirPrefix =
        "sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01/";

    // ---- VAD 設定プロパティ（実行中でも変更可）----

    public float NoiseGateDb    { get; set; } = -50f;
    public float VoiceTriggerDb { get; set; } = -35f;
    public int   SilenceDurationMs { get; set; } = 800;

    /// <summary>
    /// 音声検出前に遡って取り込むプリバッファの長さ（ミリ秒）。
    /// 発話冒頭（子音・無声区間）がトリガー前に存在する場合に補完する。
    /// デフォルト 500ms。0 で無効。
    /// </summary>
    public int PreRollMs { get; set; } = 500;

    // ---- イベント ----

    public event EventHandler<string>? TextRecognized;
    public event EventHandler<float>?  AudioLevelChanged;
    public event EventHandler<string>? StatusChanged;

    // ---- SherpaOnnx ----

    private OfflineRecognizer? _recognizer;

    // ---- NAudio ----

    private WaveInEvent? _waveIn;
    private bool         _isListening;

    // ---- VAD バッファ（音声コールバックスレッド専用）----

    private readonly List<short>   _speechBuffer = new(SampleRate * 30);
    private bool                   _isSpeaking;
    private DateTime               _lastSpeechTime;

    // ---- プリバッファ（音声検出前の音声を遡って取り込む）----
    // キューに 50ms チャンクを積み、PreRollMs 分を超えた古いチャンクを捨てるリングバッファ。
    private readonly Queue<short[]> _preRollQueue   = new();
    private int                     _preRollSamples = 0;

    // ---- 状態 ----

    public bool IsListening  => _isListening;

    private static readonly string[] RequiredFiles =
        ["tokens.txt", "README.md"];  // 最低限の存在確認

    /// <summary>モデルファイルが揃っているか（ダウンロード済みか）。</summary>
    public bool IsModelReady =>
        File.Exists(Path.Combine(ModelDir, "tokens.txt"))
        && Directory.GetFiles(ModelDir, "encoder*.onnx").Length > 0
        && Directory.GetFiles(ModelDir, "decoder*.onnx").Length > 0
        && Directory.GetFiles(ModelDir, "joiner*.onnx").Length > 0;

    // ========== 初期化 ==========

    /// <summary>
    /// モデルをダウンロード（初回のみ）してロードし、NAudio を準備する。
    /// Window.Loaded などから await する。
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        StatusChanged?.Invoke(this, "ReazonSpeech モデルを確認中...");

        if (!IsModelReady)
        {
            Directory.CreateDirectory(ModelDir);
            await DownloadAndExtractModelAsync(ct);
        }

        LoadRecognizer();
        StatusChanged?.Invoke(this, "ReazonSpeech 準備完了");
    }

    private async Task DownloadAndExtractModelAsync(CancellationToken ct)
    {
        var archivePath = Path.Combine(Path.GetTempPath(), "reazonspeech-model.tar.bz2");

        // ---- ダウンロード ----
        StatusChanged?.Invoke(this,
            "ReazonSpeech モデルをダウンロード中... (初回のみ・数百MB 程度)");

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(30);

        using (var response = await http.GetAsync(
                   ModelArchiveUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();

            var total   = response.Content.Headers.ContentLength ?? -1L;
            var tmpPath = archivePath + ".tmp";

            using (var netStream  = await response.Content.ReadAsStreamAsync(ct))
            using (var fileStream = File.Create(tmpPath))
            {
                var  buf        = new byte[81920];
                long downloaded = 0L;
                int  read;
                while ((read = await netStream.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buf, 0, read, ct);
                    downloaded += read;
                    if (total > 0)
                    {
                        var mb  = downloaded / 1_048_576.0;
                        var pct = downloaded * 100 / total;
                        StatusChanged?.Invoke(this,
                            $"ReazonSpeech ダウンロード中... {mb:F0}MB / {total / 1_048_576.0:F0}MB ({pct}%)");
                    }
                }
            }

            File.Move(tmpPath, archivePath, overwrite: true);
        }

        // ---- 展開（UI スレッドをブロックしないよう Task.Run で実行）----
        StatusChanged?.Invoke(this, "ReazonSpeech モデルを展開中...");

        await Task.Run(() =>
        {
            using var fileStream = File.OpenRead(archivePath);
            using var reader     = ReaderFactory.Open(fileStream);

            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory) continue;

                var key = (reader.Entry.Key ?? string.Empty).Replace('\\', '/');

                // トップレベルディレクトリ名を除去してフラットに配置
                var rel = key.StartsWith(ArchiveDirPrefix, StringComparison.Ordinal)
                    ? key[ArchiveDirPrefix.Length..]
                    : key;

                if (string.IsNullOrEmpty(rel)) continue;

                var dest = Path.Combine(ModelDir,
                    rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                using var entryStream  = reader.OpenEntryStream();
                using var outputStream = File.Create(dest);
                entryStream.CopyTo(outputStream);
            }
        }, ct);

        try { File.Delete(archivePath); } catch { }
    }

    private void LoadRecognizer()
    {
        // ファイルを動的検索（INT8 量子化版を優先）
        var encoder = FindOnnx("encoder")
            ?? throw new FileNotFoundException("encoder ONNX が見つかりません", ModelDir);
        var decoder = FindOnnx("decoder")
            ?? throw new FileNotFoundException("decoder ONNX が見つかりません", ModelDir);
        var joiner  = FindOnnx("joiner")
            ?? throw new FileNotFoundException("joiner ONNX が見つかりません", ModelDir);
        var tokens  = Path.Combine(ModelDir, "tokens.txt");

        var config = new OfflineRecognizerConfig();

        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 80;

        config.ModelConfig.Transducer.Encoder = encoder;
        config.ModelConfig.Transducer.Decoder = decoder;
        config.ModelConfig.Transducer.Joiner  = joiner;
        config.ModelConfig.Tokens      = tokens;
        config.ModelConfig.NumThreads  = Math.Max(1, Environment.ProcessorCount / 2);
        config.ModelConfig.Provider    = "cpu";
        config.ModelConfig.Debug       = 0;

        config.DecodingMethod  = "greedy_search";
        config.MaxActivePaths  = 4;

        _recognizer?.Dispose();
        _recognizer = new OfflineRecognizer(config);

        // NAudio 再初期化
        _waveIn?.Dispose();
        _waveIn = new WaveInEvent
        {
            WaveFormat         = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50,
        };
        _waveIn.DataAvailable += OnDataAvailable;
    }

    private string? FindOnnx(string prefix)
    {
        // INT8 量子化版を優先（CPU でより高速）
        return Directory.GetFiles(ModelDir, $"{prefix}*.int8.onnx").FirstOrDefault()
            ?? Directory.GetFiles(ModelDir, $"{prefix}*.onnx").FirstOrDefault();
    }

    // ========== 開始/停止 ==========

    public void StartListening()
    {
        if (_isListening || _waveIn == null) return;
        _waveIn.StartRecording();
        _isListening = true;
    }

    public void StopListening()
    {
        if (!_isListening) return;
        _waveIn?.StopRecording();
        _isListening = false;
        _speechBuffer.Clear();
        _isSpeaking = false;
        _preRollQueue.Clear();
        _preRollSamples = 0;
    }

    // ========== VAD（音声アクティビティ検出） ==========

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var count   = e.BytesRecorded / 2;
        var samples = new short[count];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        var rms = ComputeRms(samples);
        var db  = ToDb(rms);
        AudioLevelChanged?.Invoke(this, db);

        if (db > VoiceTriggerDb)
        {
            if (!_isSpeaking)
            {
                // 発話開始 → プリバッファを先頭に追加して冒頭音声を補完
                foreach (var chunk in _preRollQueue)
                    _speechBuffer.AddRange(chunk);
                _preRollQueue.Clear();
                _preRollSamples = 0;
            }
            _isSpeaking = true;
            _speechBuffer.AddRange(samples);
            _lastSpeechTime = DateTime.UtcNow;
        }
        else if (_isSpeaking)
        {
            if (db > NoiseGateDb)
                _speechBuffer.AddRange(samples);

            if ((DateTime.UtcNow - _lastSpeechTime).TotalMilliseconds >= SilenceDurationMs)
                FlushSpeechBuffer();
        }
        else
        {
            // 非発話中 → PreRollMs 分のローリングバッファを維持
            if (PreRollMs > 0)
            {
                _preRollQueue.Enqueue(samples);
                _preRollSamples += samples.Length;

                // 古いチャンクを溢れた分だけ捨てる
                var maxSamples = SampleRate * PreRollMs / 1000;
                while (_preRollSamples > maxSamples && _preRollQueue.Count > 0)
                {
                    var removed = _preRollQueue.Dequeue();
                    _preRollSamples -= removed.Length;
                }
            }
        }
    }

    private void FlushSpeechBuffer()
    {
        if (_speechBuffer.Count < (int)(SampleRate * 0.3))
        {
            _speechBuffer.Clear();
            _isSpeaking = false;
            return;
        }

        var samples = _speechBuffer.ToArray();
        _speechBuffer.Clear();
        _isSpeaking = false;

        StatusChanged?.Invoke(this, "音声処理中...");

        // バックグラウンドで推論（NAudio コールバックをブロックしない）
        _ = Task.Run(() => Recognize(samples));
    }

    // ========== SherpaOnnx 推論 ==========

    private void Recognize(short[] samples)
    {
        if (_recognizer == null) return;

        try
        {
            var floats = SamplesToFloat(samples);
            var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(SampleRate, floats);

            // OfflineRecognizer の結果は recognizer.GetResult() ではなく
            // Decode() 後に stream.Result プロパティから取得する
            _recognizer.Decode(stream);

            var text = stream.Result.Text.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                TextRecognized?.Invoke(this, text);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this,
                $"ReazonSpeech 認識エラー [{ex.GetType().Name}]: {ex.Message}");
        }
    }

    // ========== ユーティリティ ==========

    private static float[] SamplesToFloat(short[] samples)
    {
        var floats = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            floats[i] = samples[i] / 32768.0f;
        return floats;
    }

    private static float ComputeRms(short[] samples)
    {
        if (samples.Length == 0) return 0f;
        var sum = 0.0;
        foreach (var s in samples) sum += (double)s * s;
        return (float)(Math.Sqrt(sum / samples.Length) / 32768.0);
    }

    private static float ToDb(float rms) =>
        rms > 0f ? 20f * MathF.Log10(rms) : -96f;

    // ========== IDisposable ==========

    public void Dispose()
    {
        StopListening();
        _recognizer?.Dispose();
        _waveIn?.Dispose();
    }
}
