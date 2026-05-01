using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIVoiceBridge.Core;

namespace AIVoiceBridge.Plugin.AIVoice;

/// <summary>
/// A.I.VOICE v1 プラグイン。
///
/// AI.Talk.Editor.Api は System.ServiceModel (WCF/.NET Framework) に依存するため
/// .NET 8 から直接呼び出せない。
/// そのため .NET Framework 4.8 製ブリッジプロセス（AIVoiceBridge.AIVoice1Bridge.exe）経由で通信する。
///
/// 動作フロー:
///   1. ConnectAsync(): --list-casts でボイスプリセット一覧を取得
///   2. SynthesizeAsync(): --speak コマンドで WAV を生成しバイト列を返す
///   3. MainViewModel が WAV を AudioOutputService（選択中の出力デバイス）で再生
/// </summary>
public sealed class AIVoicePlugin : IVoiceSynthesizerPlugin
{
    private string? _bridgePath;
    private readonly List<CastInfo> _casts = [];

    public string Name => "A.I.VOICE";
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
                    "AIVoiceBridge.AIVoice1Bridge.exe が見つかりません。\n" +
                    "ソリューションをビルドしてブリッジを生成してください。");

            var output = RunBridge(_bridgePath, ["--list-casts"], timeoutMs: 25000);

            _casts.Clear();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = line.Trim();
                if (!string.IsNullOrEmpty(name))
                    _casts.Add(new CastInfo(name));
            }

            if (_casts.Count == 0)
                throw new InvalidOperationException(
                    "A.I.VOICE に利用可能なボイスプリセットが見つかりません。\n" +
                    "A.I.VOICE が起動しているか、ボイスがインストールされているか確認してください。");

            if (CurrentCast == null)
                CurrentCast = _casts[0].Name;
        });
    }

    public Task DisconnectAsync() => Task.CompletedTask;

    public IReadOnlyList<CastInfo> GetAvailableCasts() => _casts.AsReadOnly();

    // A.I.VOICE は自身のオーディオ出力で直接再生するため WAV 合成は不要
    public Task<byte[]?> SynthesizeAsync(string text) => Task.FromResult<byte[]?>(null);

    public async Task SpeakAsync(string text)
    {
        if (_bridgePath == null || string.IsNullOrWhiteSpace(text)) return;

        var tmpText = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpText, text, Encoding.UTF8);

            var cast       = CurrentCast ?? _casts.FirstOrDefault()?.Name ?? string.Empty;
            var speed      = Fmt(_options.Speed);
            var volume     = Fmt(_options.Volume);
            var pitch      = Fmt(_options.Pitch);
            var pitchRange = Fmt(_options.Intonation);

            // ブリッジが Play() → WaitUntilIdle() まで待ってから戻る（再生完了同期）
            await Task.Run(() => RunBridge(_bridgePath, [
                "--speak", tmpText, cast, speed, volume, pitch, pitchRange
            ], timeoutMs: 120_000));
        }
        finally
        {
            try { File.Delete(tmpText); } catch { }
        }
    }

    private static string Fmt(double v) =>
        v.ToString("F2", CultureInfo.InvariantCulture);

    private static string? FindBridgePath()
    {
        var pluginDir = Path.GetDirectoryName(typeof(AIVoicePlugin).Assembly.Location);
        if (pluginDir == null) return null;

        var path = Path.Combine(pluginDir, "AIVoiceBridge.AIVoice1Bridge.exe");
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
            StandardErrorEncoding  = Encoding.UTF8,
            WorkingDirectory       = Path.GetDirectoryName(bridgePath)!,
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
            throw new TimeoutException("AIVoice1Bridge がタイムアウトしました。");
        }

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"AIVoice1Bridge がエラーで終了しました (code={proc.ExitCode}): {stderr.Trim()}");

        return stdout;
    }

    public void Dispose() { }
}
