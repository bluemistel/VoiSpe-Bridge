using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using VoiSpeBridge.Core;

namespace VoiSpeBridge.Plugin.CeVIOAI;

/// <summary>
/// CeVIO AI プラグイン。
///
/// CeVIO AI の RemoteService2 は System.Runtime.Remoting（.NET Remoting）を使用しており
/// .NET 8 では動作しないため、.NET Framework 4.8 製ブリッジプロセス経由で通信する。
///
/// 動作フロー:
///   1. ConnectAsync(): AIVoiceBridge.CeVIOBridge.exe --list-casts でキャスト一覧を取得
///   2. RefreshEmotionsAsync(): --list-emotions <cast> で感情一覧を取得
///   3. SynthesizeAsync(): --speak コマンドで WAV を生成しバイト列を返す
///   4. MainViewModel が WAV を AudioOutputService（選択中の出力デバイス）で再生
/// </summary>
public sealed class CeVIOAIPlugin : IVoiceSynthesizerPlugin, IPluginWithEmotions
{
    private string? _bridgePath;
    private readonly List<CastInfo> _casts = [];

    // ---- 感情パラメータ ----

    // キャッシュ済み感情一覧（RefreshEmotionsAsync() で更新）
    private readonly List<EmotionParameter> _emotionParameters = [];
    // 現在の感情値（SetEmotion() で更新、SynthesizeAsync() で送信）
    private readonly Dictionary<string, double> _emotionValues = [];

    public string Name => "CeVIO AI";
    public string Version => "1.0.0";

    public bool IsConnected => _bridgePath != null && File.Exists(_bridgePath);

    public string? CurrentCast { get; set; }

    private SynthesisOptions _options = SynthesisOptions.Default;
    public SynthesisOptions Options
    {
        get => _options;
        set => _options = value;
    }

    public Task ConnectAsync()
    {
        return Task.Run(() =>
        {
            _bridgePath = FindBridgePath()
                ?? throw new InvalidOperationException(
                    "AIVoiceBridge.CeVIOBridge.exe が見つかりません。\n" +
                    "ソリューションをビルドしてブリッジを生成してください。");

            var output = RunBridge(_bridgePath, ["--list-casts"], timeoutMs: 20000);

            _casts.Clear();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = line.Trim();
                if (!string.IsNullOrEmpty(name))
                    _casts.Add(new CastInfo(name));
            }

            if (_casts.Count == 0)
                throw new InvalidOperationException(
                    "CeVIO AI に利用可能なキャストが見つかりません。\n" +
                    "CeVIO AI が起動しているか、ボイスがインストールされているか確認してください。");

            if (CurrentCast == null)
                CurrentCast = _casts[0].Name;
        });
    }

    public Task DisconnectAsync() => Task.CompletedTask;

    public IReadOnlyList<CastInfo> GetAvailableCasts() => _casts.AsReadOnly();

    // ---- IPluginWithEmotions ----

    /// <summary>
    /// 現在のキャストの感情一覧をブリッジ経由で取得してキャッシュする。
    /// 出力フォーマット: "EmotionName\tValue\n" （タブ区切り）
    /// </summary>
    public Task RefreshEmotionsAsync()
    {
        return Task.Run(() =>
        {
            _emotionParameters.Clear();

            if (_bridgePath == null) return;
            var cast = CurrentCast ?? string.Empty;
            if (string.IsNullOrEmpty(cast)) return;

            try
            {
                var output = RunBridge(_bridgePath, ["--list-emotions", cast], timeoutMs: 20000);

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    var parts = trimmed.Split('\t', 2);
                    var emotionName = parts[0].Trim();
                    if (string.IsNullOrEmpty(emotionName)) continue;

                    // API から返ってきたデフォルト値（ユーザーが未設定の場合はこの値を使う）
                    double apiDefault = parts.Length >= 2 && double.TryParse(parts[1], out var v) ? v : 50.0;
                    // ユーザーが SetEmotion() で変更済みなら、その値を優先する
                    var currentValue = _emotionValues.GetValueOrDefault(emotionName, apiDefault);

                    _emotionParameters.Add(new EmotionParameter(
                        Key:   emotionName,
                        Label: emotionName,
                        Value: currentValue,
                        Min:   0.0,
                        Max:   100.0));
                }
            }
            catch
            {
                // 感情取得失敗（ボイスが感情未対応など）は無視してリストを空のままにする
                _emotionParameters.Clear();
            }
        });
    }

    public IReadOnlyList<EmotionParameter> GetEmotions() => _emotionParameters.AsReadOnly();

    public void SetEmotion(string key, double value)
        => _emotionValues[key] = Math.Clamp(value, 0.0, 100.0);

    // ---- 合成 ----

    public async Task<byte[]?> SynthesizeAsync(string text)
    {
        if (_bridgePath == null || string.IsNullOrWhiteSpace(text)) return null;

        // Speed:     SynthesisOptions 0.5-2.0 (標準 1.0) → CeVIO 0-100 (標準 50)
        var speed     = ClampUint(_options.Speed * 50.0);
        // Tone:      SynthesisOptions 0.5-2.0 (標準 1.0) → CeVIO 0-100 (標準 50)
        var tone      = ClampUint(_options.Pitch * 50.0);
        // Volume:    SynthesisOptions 0.0-2.0 (標準 1.0) → CeVIO 0-100 (標準 100)
        var volume    = ClampUint(_options.Volume * 100.0);
        // ToneScale: SynthesisOptions 0.0-2.0 (標準 1.0) → CeVIO 0-100 (標準 100)
        var toneScale = ClampUint(_options.Intonation * 100.0);

        var tmpText = Path.GetTempFileName();
        var tmpWav  = Path.ChangeExtension(Path.GetTempFileName(), ".wav");

        try
        {
            await File.WriteAllTextAsync(tmpText, text, Encoding.UTF8);

            var cast = CurrentCast ?? _casts.FirstOrDefault()?.Name ?? string.Empty;

            // 基本引数
            var speakArgs = new List<string>
            {
                "--speak", tmpText, tmpWav,
                cast,
                speed.ToString(), tone.ToString(), volume.ToString(), toneScale.ToString()
            };

            // 感情引数を追加（"EmotionName=Value" 形式）
            foreach (var ep in _emotionParameters)
            {
                var val = (uint)Math.Clamp(Math.Round(_emotionValues.GetValueOrDefault(ep.Key, ep.Value)), 0, 100);
                speakArgs.Add($"{ep.Key}={val}");
            }

            await Task.Run(() => RunBridge(_bridgePath, [.. speakArgs], timeoutMs: 60000));

            if (!File.Exists(tmpWav))
                throw new InvalidOperationException("CeVIO AI の WAV 生成に失敗しました。");

            return await File.ReadAllBytesAsync(tmpWav);
        }
        finally
        {
            try { File.Delete(tmpText); } catch { }
            try { File.Delete(tmpWav); } catch { }
        }
    }

    // SynthesizeAsync が WAV を返すため SpeakAsync は通常呼ばれない
    public Task SpeakAsync(string text) => Task.CompletedTask;

    private static string? FindBridgePath()
    {
        // プラグイン DLL と同じフォルダにブリッジ EXE を探す
        var pluginDir = Path.GetDirectoryName(typeof(CeVIOAIPlugin).Assembly.Location);
        if (pluginDir == null) return null;

        var path = Path.Combine(pluginDir, "AIVoiceBridge.CeVIOBridge.exe");
        return File.Exists(path) ? path : null;
    }

    private static string RunBridge(string bridgePath, string[] args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(bridgePath)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            // ブリッジの作業ディレクトリを plugins フォルダに設定
            // (.NET Framework がそこから CeVIO.Talk.RemoteService2.dll を解決する)
            WorkingDirectory = Path.GetDirectoryName(bridgePath)!,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(); } catch { }
            throw new TimeoutException("CeVIOBridge がタイムアウトしました。");
        }

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"CeVIOBridge がエラーで終了しました (code={proc.ExitCode}): {stderr.Trim()}");

        return stdout;
    }

    private static uint ClampUint(double value)
        => (uint)Math.Clamp(Math.Round(value), 0, 100);

    public void Dispose() { }
}

// List<T>.FirstOrDefault の拡張（LINQ なしで動作）
file static class ListExtensions
{
    public static T? FirstOrDefault<T>(this List<T> list) where T : class
        => list.Count > 0 ? list[0] : null;
}
