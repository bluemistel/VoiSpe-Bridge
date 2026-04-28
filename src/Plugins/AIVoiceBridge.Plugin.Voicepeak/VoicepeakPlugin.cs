using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIVoiceBridge.Core;

namespace AIVoiceBridge.Plugin.Voicepeak;

/// <summary>
/// VOICEPEAK プラグイン。
///
/// VOICEPEAK は公式 CLI インターフェース (voicepeak.exe) を提供しており、
/// テキストファイルを入力して WAV ファイルとして出力する方式で合成する。
///
/// 動作フロー:
///   1. ConnectAsync() で voicepeak.exe を探し、--list-narrator でナレーター一覧を取得
///   2. SynthesizeAsync() でテンポラリファイルに書き出し → voicepeak.exe を呼び出し → WAV バイト列を返す
///   3. MainViewModel が WAV バイト列を AudioOutputService（選択中の出力デバイス）で再生
/// </summary>
public sealed class VoicepeakPlugin : IVoiceSynthesizerPlugin
{
    private static readonly string[] _defaultPaths =
    [
        @"C:\Program Files\VOICEPEAK\voicepeak.exe",
        @"C:\Program Files (x86)\VOICEPEAK\voicepeak.exe",
    ];

    public string Name => "VOICEPEAK";
    public string Version => "1.0.0";

    private string? _exePath;
    private readonly List<CastInfo> _narrators = [];

    public bool IsConnected => _exePath != null && File.Exists(_exePath);

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
            _exePath = _defaultPaths.FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException(
                    "VOICEPEAK が見つかりません。インストール済みか確認してください。\n" +
                    "確認先: C:\\Program Files\\VOICEPEAK\\voicepeak.exe");

            var output = RunCommand(_exePath, "--list-narrator", timeoutMs: 15000);
            _narrators.Clear();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = line.Trim();
                if (!string.IsNullOrEmpty(name))
                    _narrators.Add(new CastInfo(name));
            }

            if (CurrentCast == null && _narrators.Count > 0)
                CurrentCast = _narrators[0].Name;
        });
    }

    public Task DisconnectAsync() => Task.CompletedTask;

    public IReadOnlyList<CastInfo> GetAvailableCasts() => _narrators.AsReadOnly();

    public async Task<byte[]?> SynthesizeAsync(string text)
    {
        if (_exePath == null || string.IsNullOrWhiteSpace(text)) return null;

        // Speed: SynthesisOptions.Speed (0.5–2.0, standard 1.0) → VOICEPEAK (50–200, standard 100)
        var speed = (int)Math.Clamp(_options.Speed * 100.0, 50, 200);
        // Pitch: SynthesisOptions.Pitch (0.5–2.0, standard 1.0) → VOICEPEAK (-300–+300, standard 0)
        var pitch = (int)Math.Clamp((_options.Pitch - 1.0) * 600.0, -300, 300);

        var tmpText = Path.GetTempFileName();
        // voicepeak.exe -o では拡張子で形式を判断するため .wav が必須
        var tmpWav = Path.ChangeExtension(Path.GetTempFileName(), ".wav");

        try
        {
            await File.WriteAllTextAsync(tmpText, text, Encoding.UTF8);

            var args = new StringBuilder();
            args.Append($"-t \"{tmpText}\" -o \"{tmpWav}\" --speed {speed} --pitch {pitch}");

            var narrator = CurrentCast ?? _narrators.FirstOrDefault()?.Name;
            if (!string.IsNullOrEmpty(narrator))
                args.Append($" -n \"{narrator}\"");

            await Task.Run(() => RunCommand(_exePath, args.ToString(), timeoutMs: 60000));

            if (!File.Exists(tmpWav))
                return null;

            return await File.ReadAllBytesAsync(tmpWav);
        }
        finally
        {
            try { File.Delete(tmpText); } catch { }
            try { File.Delete(tmpWav); } catch { }
        }
    }

    // SynthesizeAsync が WAV を返すため SpeakAsync は呼ばれない
    public Task SpeakAsync(string text) => Task.CompletedTask;

    private static string RunCommand(string exe, string args, int timeoutMs)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            }
        };

        proc.Start();
        var stdout = proc.StandardOutput.ReadToEnd();

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(); } catch { }
            throw new TimeoutException($"VOICEPEAK がタイムアウトしました (args: {args})");
        }

        if (proc.ExitCode != 0)
        {
            var stderr = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"VOICEPEAK がエラーで終了しました (code={proc.ExitCode}): {stderr}");
        }

        return stdout;
    }

    public void Dispose() { }
}
