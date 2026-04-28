using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI.Talk.Editor.Api;
using AIVoiceBridge.Core;

namespace AIVoiceBridge.Plugin.AIVoice;

/// <summary>
/// A.I.VOICE (v1) Editor API を使用するプラグイン。
///
/// 前提条件:
///   - A.I.VOICE がインストール済みであること
///   - A.I.VOICE Editor が起動していること（またはConnectAsync内で自動起動）
///   - AI.Talk.Editor.Api.dll が plugins フォルダに存在すること
///     コピー元: C:\Program Files\AI\AIVoice\AIVoiceEditor\AI.Talk.Editor.Api.dll
///
/// 動作:
///   SynthesizeAsync() が WAV データを返す → アプリが選択デバイスで再生 → OBS取り込み可能
/// </summary>
public sealed class AIVoicePlugin : IVoiceSynthesizerPlugin
{
    private TtsControl? _tts;
    private bool _hostStartedByUs;

    public string Name => "A.I.VOICE";
    public string Version => "1.0.0";
    public bool IsConnected => _tts?.Status is HostStatus.Idle or HostStatus.Busy;

    private SynthesisOptions _options = SynthesisOptions.Default;
    public SynthesisOptions Options
    {
        get => _options;
        set
        {
            _options = value;
            ApplyVoicePresetOptions(value);
        }
    }

    public string? CurrentCast
    {
        get => _tts?.CurrentVoicePresetName;
        set
        {
            if (_tts != null && value != null)
                _tts.CurrentVoicePresetName = value;
        }
    }

    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            _tts?.Disconnect();
            try { _tts = new TtsControl(); }
            catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException
                                            or System.IO.FileLoadException)
            {
                throw new InvalidOperationException(
                    "A.I.VOICE v1 の依存ライブラリが読み込めません（System.ServiceModel 等）。" +
                    "A.I.VOICE2 をお使いの場合はプラグインドロップダウンから「A.I.VOICE2」を選択してください。", ex);
            }

            // 利用可能なホスト（A.I.VOICE Editorプロセス）を取得
            var hosts = _tts.GetAvailableHostNames();
            if (hosts.Length == 0)
                throw new InvalidOperationException(
                    "A.I.VOICE Editor が見つかりません。A.I.VOICE をインストールしてください。");

            // 先頭のホストを使用（通常は1つだけ）
            _tts.Initialize(hosts[0]);

            // Editorが起動していない場合は自動起動
            if (_tts.Status == HostStatus.NotRunning)
            {
                _tts.StartHost();
                _hostStartedByUs = true;

                // 起動完了を最大10秒待機
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (_tts.Status == HostStatus.NotRunning && DateTime.UtcNow < deadline)
                    Thread.Sleep(200);
            }

            _tts.Connect();

            // 接続確立を待機
            var connectDeadline = DateTime.UtcNow.AddSeconds(5);
            while (_tts.Status == HostStatus.NotConnected && DateTime.UtcNow < connectDeadline)
                Thread.Sleep(200);

            if (_tts.Status == HostStatus.NotConnected)
                throw new TimeoutException("A.I.VOICE Editor への接続がタイムアウトしました。");
        });
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            _tts?.Disconnect();
            // 自分で起動した場合のみ終了させる（ユーザーが開いていた場合は残す）
            if (_hostStartedByUs)
            {
                _tts?.TerminateHost();
                _hostStartedByUs = false;
            }
        });
    }

    public IReadOnlyList<CastInfo> GetAvailableCasts()
    {
        if (_tts == null) return [];

        var casts = new List<CastInfo>();
        // VoicePresetNames = キャラクター名＋感情スタイルのプリセット
        foreach (var name in _tts.VoicePresetNames)
            casts.Add(new CastInfo(name));
        return casts;
    }

    /// <summary>
    /// テキストを WAV ファイル経由で合成データとして返す。
    /// アプリ側が選択した出力デバイス（仮想オーディオケーブル等）で再生する。
    /// </summary>
    public async Task<byte[]?> SynthesizeAsync(string text)
    {
        EnsureConnected();

        var tmpPath = Path.Combine(Path.GetTempPath(), $"aivoicebridge_{Guid.NewGuid():N}.wav");
        try
        {
            await Task.Run(() =>
            {
                // 再生完了まで待機してから WAV を保存
                WaitUntilIdle();
                _tts!.Text = text;
                _tts.SaveAudioToFile(tmpPath);
            });

            return await File.ReadAllBytesAsync(tmpPath);
        }
        finally
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
    }

    /// <summary>
    /// A.I.VOICE Editor の出力デバイスで直接再生するフォールバック。
    /// SynthesizeAsync() が失敗した場合や出力デバイスを使いたい場合に使用。
    /// </summary>
    public async Task SpeakAsync(string text)
    {
        EnsureConnected();

        await Task.Run(() =>
        {
            WaitUntilIdle();
            _tts!.Text = text;
            _tts.Play();

            // 再生完了まで待機（最大60秒）
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (_tts.Status == HostStatus.Busy && DateTime.UtcNow < deadline)
                Thread.Sleep(100);
        });
    }

    private void WaitUntilIdle(int timeoutSec = 30)
    {
        if (_tts == null) return;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (_tts.Status == HostStatus.Busy && DateTime.UtcNow < deadline)
            Thread.Sleep(100);
    }

    private void EnsureConnected()
    {
        if (_tts == null || !IsConnected)
            throw new InvalidOperationException("ConnectAsync() を先に呼んでください。");
    }

    private void ApplyVoicePresetOptions(SynthesisOptions opts)
    {
        if (_tts == null || string.IsNullOrEmpty(_tts.CurrentVoicePresetName)) return;

        // ボイスプリセットのJSONを取得して速度・音量・ピッチを書き換える
        // プリセットのJSON形式: {"Speed":1.0,"Volume":1.0,"Pitch":1.0,"PitchRange":1.0,...}
        try
        {
            var json = _tts.GetVoicePreset(_tts.CurrentVoicePresetName);
            if (string.IsNullOrEmpty(json)) return;

            // System.Text.Json で書き換え（外部依存なし）
            json = SetJsonNumber(json, "Speed", opts.Speed);
            json = SetJsonNumber(json, "Volume", opts.Volume);
            json = SetJsonNumber(json, "Pitch", opts.Pitch);
            json = SetJsonNumber(json, "PitchRange", opts.Intonation);

            _tts.SetVoicePreset(json);
        }
        catch
        {
            // パラメータ適用失敗はサイレントに無視（APIバージョン差異）
        }
    }

    private static string SetJsonNumber(string json, string key, double value)
    {
        // 簡易JSONフィールド置換（正規表現なし）
        var search = $"\"{key}\":";
        var idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return json;

        var start = idx + search.Length;
        // 値の終端を探す（カンマまたは }）
        var end = start;
        while (end < json.Length && json[end] != ',' && json[end] != '}')
            end++;

        return json[..start] + value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + json[end..];
    }

    public void Dispose()
    {
        try { DisconnectAsync().GetAwaiter().GetResult(); } catch { }
        _tts = null;
    }
}
