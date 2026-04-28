using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.IO;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace AIVoiceBridge.App.Services;

public enum RecognitionModelType
{
    /// <summary>Tiny (~77MB) 最速、精度低</summary>
    Tiny,
    /// <summary>Small (~467MB) バランス型、日本語に推奨</summary>
    Small,
    /// <summary>Medium (~1.5GB) 高精度、低速</summary>
    Medium,
}

/// <summary>
/// OpenAI Whisper（ローカル実行）を使った日本語特化音声認識サービス。
///
/// フロー:
///   NAudio WaveInEvent (16kHz, 16bit, mono)
///     → RMS による VAD（無音区間で自動確定）
///     → Channel 経由で非同期 Whisper 推論
///     → バージョン管理で最新発話のみ結果を採用
///     → TextRecognized イベント発火
/// </summary>
public sealed class SpeechRecognitionService : IDisposable
{
    private const int SampleRate = 16000;

    // ---- 設定プロパティ（実行中でも変更可） ----

    public float NoiseGateDb { get; set; } = -50f;
    public float VoiceTriggerDb { get; set; } = -35f;
    public int SilenceDurationMs { get; set; } = 800;

    /// <summary>
    /// true: GPU（CUDA → Vulkan → CPU の順でフォールバック）
    /// false: CPU のみ
    /// </summary>
    public bool UseGpu { get; set; } = true;

    // ---- イベント ----

    public event EventHandler<string>? TextRecognized;
    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<string>? StatusChanged;

    // ---- 状態 ----

    public bool IsListening => _isListening;

    // ---- プライベート ----

    private WaveInEvent? _waveIn;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private bool _isListening;

    // VAD バッファ（音声コールバックスレッドのみからアクセス）
    private readonly List<short> _speechBuffer = new(SampleRate * 30);
    private bool _isSpeaking;
    private DateTime _lastSpeechTime;

    // 最新発話バージョン — 古い Whisper 推論結果を破棄するために使用
    private int _currentVersion;

    // 非同期処理パイプライン
    private readonly Channel<(short[] Samples, int Version)> _channel;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    public SpeechRecognitionService()
    {
        _channel = Channel.CreateBounded<(short[], int)>(
            new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    // ========== 初期化 ==========

    public async Task InitializeAsync(
        RecognitionModelType model = RecognitionModelType.Small,
        CancellationToken ct = default)
    {
        var ggml = ToGgmlType(model);
        var modelPath = GetModelPath(ggml);

        if (!File.Exists(modelPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
            StatusChanged?.Invoke(this,
                $"Whisper {model} モデルをダウンロード中... (初回のみ・数分かかります)");

            using var stream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggml, cancellationToken: ct);
            using var file = File.OpenWrite(modelPath);
            await stream.CopyToAsync(file, ct);
        }

        // GPU/CPU ランタイムの優先順位を設定
        // RuntimeOptions はファクトリ生成前に設定する必要がある
        RuntimeOptions.RuntimeLibraryOrder = UseGpu
            ? [RuntimeLibrary.Cuda, RuntimeLibrary.Vulkan,
               RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]
            : [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];

        var modeLabel = UseGpu ? "GPU 優先" : "CPU のみ";
        StatusChanged?.Invoke(this, $"Whisper {model} モデルを読み込み中... ({modeLabel})");

        _processor?.Dispose();
        _factory?.Dispose();

        var factoryOptions = new WhisperFactoryOptions { UseGpu = UseGpu, GpuDevice = 0 };
        _factory = WhisperFactory.FromPath(modelPath, factoryOptions);
        _processor = _factory.CreateBuilder()
            .WithLanguage("ja")
            .WithNoContext()
            .WithNoSpeechThreshold(0.6f)
            .Build();

        _waveIn?.Dispose();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50, // 100ms→50ms でレスポンスを改善
        };
        _waveIn.DataAvailable += OnDataAvailable;

        StatusChanged?.Invoke(this, "音声認識準備完了");
    }

    // ========== 開始/停止 ==========

    public void StartListening()
    {
        if (_isListening || _waveIn == null) return;

        _cts = new CancellationTokenSource();
        _processingTask = RunProcessingLoopAsync(_cts.Token);
        _waveIn.StartRecording();
        _isListening = true;
    }

    public void StopListening()
    {
        if (!_isListening) return;

        _waveIn?.StopRecording();
        _cts?.Cancel();
        _isListening = false;

        _speechBuffer.Clear();
        _isSpeaking = false;
        Interlocked.Increment(ref _currentVersion); // 処理中の推論結果を破棄
    }

    // ========== VAD（音声アクティビティ検出） ==========

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var count = e.BytesRecorded / 2;
        var samples = new short[count];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        var rms = ComputeRms(samples);
        var db = ToDb(rms);
        AudioLevelChanged?.Invoke(this, db);

        if (db > VoiceTriggerDb)
        {
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
    }

    private void FlushSpeechBuffer()
    {
        if (_speechBuffer.Count >= (int)(SampleRate * 0.3))
        {
            var samples = _speechBuffer.ToArray();

            // バージョンを更新して古い推論結果を無効化
            var version = Interlocked.Increment(ref _currentVersion);

            // チャンネルの未処理アイテムを破棄（最新のみ処理する）
            while (_channel.Reader.TryRead(out _)) { }

            _channel.Writer.TryWrite((samples, version));
            StatusChanged?.Invoke(this, "音声処理中...");
        }

        _speechBuffer.Clear();
        _isSpeaking = false;
    }

    // ========== 非同期 Whisper 推論 ==========

    private async Task RunProcessingLoopAsync(CancellationToken ct)
    {
        await foreach (var (samples, version) in _channel.Reader.ReadAllAsync(ct))
        {
            try { await RecognizeAsync(samples, version, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"認識エラー [{ex.GetType().Name}]: {ex.Message}");
            }
        }
    }

    private async Task RecognizeAsync(short[] samples, int version, CancellationToken ct)
    {
        if (_processor == null) return;

        var floats = SamplesToFloat(samples);
        var sb = new StringBuilder();

        await foreach (var seg in _processor.ProcessAsync(floats, ct))
        {
            var text = seg.Text.Trim();
            if (!string.IsNullOrEmpty(text) && !IsHallucination(text))
                sb.Append(text);
        }

        // 推論中に新しい発話が来ていた場合は結果を破棄
        if (version != Volatile.Read(ref _currentVersion)) return;

        var result = sb.ToString().Trim();
        if (!string.IsNullOrEmpty(result))
            TextRecognized?.Invoke(this, result);
    }

    private static readonly string[] _hallucinations =
    [
        "ご視聴ありがとう", "字幕は自動生成", "チャンネル登録", "よろしくお願いします",
        "お疲れ様でした", "Thank you for watching", "Subscribe",
    ];

    private static bool IsHallucination(string text) =>
        Array.Exists(_hallucinations, h => text.Contains(h, StringComparison.OrdinalIgnoreCase));

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

    private static GgmlType ToGgmlType(RecognitionModelType m) => m switch
    {
        RecognitionModelType.Tiny => GgmlType.Tiny,
        RecognitionModelType.Small => GgmlType.Small,
        RecognitionModelType.Medium => GgmlType.Medium,
        _ => GgmlType.Small,
    };

    private static string GetModelPath(GgmlType type)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIVoiceBridge", "models");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"ggml-{type.ToString().ToLower()}.bin");
    }

    public void Dispose()
    {
        StopListening();
        _cts?.Dispose();
        _channel.Writer.TryComplete();
        _processor?.Dispose();
        _factory?.Dispose();
        _waveIn?.Dispose();
    }
}
