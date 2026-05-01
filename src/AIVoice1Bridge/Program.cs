using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

// A.I.VOICE v1 の AI.Talk.Editor.Api は System.ServiceModel (WCF) に依存するため
// .NET Framework 4.8 で動作させる必要がある。
// このブリッジは AIVoiceBridge.Plugin.AIVoice.dll (.NET 8) からサブプロセスとして起動され、
// A.I.VOICE Editor との通信を代行する。

#if USE_REAL_AIVOICE_API
using AI.Talk.Editor.Api;
#endif

namespace AIVoiceBridge.AIVoice1Bridge;

static class Program
{
    static int Main(string[] args)
    {
        // stdout / stderr を UTF-8 に統一（プラグイン側の StandardErrorEncoding = UTF8 と対応）
        Console.OutputEncoding = Encoding.UTF8;
        Console.Error.Close();
        Console.SetError(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });

        if (args.Length == 0)
            return Error("引数が必要です。--list-casts または --speak");

#if !USE_REAL_AIVOICE_API
        return Error("AI.Talk.Editor.Api.dll が見つかりません。" +
            "src\\Plugins\\AIVoiceBridge.Plugin.AIVoice\\lib\\ に配置してリビルドしてください。");
#else
        return args[0] switch
        {
            "--list-casts" => ListCasts(),
            "--speak"      => Speak(args),
            _              => Error($"不明なコマンド: {args[0]}")
        };
#endif
    }

#if USE_REAL_AIVOICE_API

    // --list-casts
    // 標準出力に利用可能なボイスプリセット名を1行ずつ出力する。
    static int ListCasts()
    {
        TtsControl? tts = null;
        try
        {
            tts = ConnectToEditor();
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        var presets = tts.VoicePresetNames;
        if (presets == null || presets.Length == 0)
            return Error("利用可能なボイスプリセットがありません。A.I.VOICE のボイスをインストールしてください。");

        foreach (var name in presets)
            Console.WriteLine(name);

        tts.Disconnect();
        return 0;
    }

    // --speak <textfile> <presetname> <speed> <volume> <pitch> <pitchrange>
    // WAV ファイルは生成せず、A.I.VOICE Editor の Play() で直接再生する。
    static int Speak(string[] args)
    {
        if (args.Length < 7)
            return Error("--speak の引数が不足しています。");

        var textFile   = args[1];
        var preset     = args[2];
        var speed      = TryParseDouble(args[3], 1.0);
        var volume     = TryParseDouble(args[4], 1.0);
        var pitch      = TryParseDouble(args[5], 1.0);
        var pitchRange = TryParseDouble(args[6], 1.0);

        if (!File.Exists(textFile))
            return Error($"テキストファイルが見つかりません: {textFile}");

        var text = File.ReadAllText(textFile, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
            return Error("テキストが空です。");

        TtsControl tts;
        try { tts = ConnectToEditor(); }
        catch (Exception ex) { return Error(ex.Message); }

        try
        {
            if (!string.IsNullOrEmpty(preset))
                tts.CurrentVoicePresetName = preset;

            ApplyOptions(tts, speed, volume, pitch, pitchRange);

            // 前の再生が終わるまで待機
            WaitUntilIdle(tts, 10);

            tts.Text = text;
            tts.Play();

            // 再生完了まで待機（最大120秒）
            WaitUntilIdle(tts, 120);
        }
        catch (Exception ex)
        {
            return Error($"再生に失敗しました: {ex.Message}");
        }
        finally
        {
            tts.Disconnect();
        }

        return 0;
    }

    // A.I.VOICE Editor に接続して TtsControl を返す。失敗時は例外を投げる。
    static TtsControl ConnectToEditor()
    {
        TtsControl tts;
        try
        {
            tts = new TtsControl();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"TtsControl の初期化に失敗しました（A.I.VOICE がインストールされているか確認してください）: {ex.Message}", ex);
        }

        var hosts = tts.GetAvailableHostNames();
        if (hosts == null || hosts.Length == 0)
            throw new InvalidOperationException(
                "A.I.VOICE Editor が見つかりません。A.I.VOICE をインストールしてください。");

        tts.Initialize(hosts[0]);

        // Editor が起動していない場合は自動起動
        if (tts.Status == HostStatus.NotRunning)
        {
            tts.StartHost();

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (tts.Status == HostStatus.NotRunning && DateTime.UtcNow < deadline)
                Thread.Sleep(300);

            if (tts.Status == HostStatus.NotRunning)
                throw new TimeoutException("A.I.VOICE Editor の起動がタイムアウトしました。");
        }

        tts.Connect();

        // 接続確立を待機（最大5秒）
        var connectDeadline = DateTime.UtcNow.AddSeconds(5);
        while (tts.Status == HostStatus.NotConnected && DateTime.UtcNow < connectDeadline)
            Thread.Sleep(100);

        if (tts.Status == HostStatus.NotConnected)
            throw new TimeoutException("A.I.VOICE Editor への接続がタイムアウトしました。");

        return tts;
    }

    static void ApplyOptions(TtsControl tts, double speed, double volume, double pitch, double pitchRange)
    {
        try
        {
            var presetName = tts.CurrentVoicePresetName;
            if (string.IsNullOrEmpty(presetName)) return;

            var json = tts.GetVoicePreset(presetName);
            if (string.IsNullOrEmpty(json)) return;

            json = SetJsonDouble(json, "Speed",      speed);
            json = SetJsonDouble(json, "Volume",     volume);
            json = SetJsonDouble(json, "Pitch",      pitch);
            json = SetJsonDouble(json, "PitchRange", pitchRange);

            tts.SetVoicePreset(json);
        }
        catch
        {
            // パラメータ適用失敗はサイレントに無視（APIバージョン差異）
        }
    }

    static void WaitUntilIdle(TtsControl tts, int timeoutSec)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (tts.Status == HostStatus.Busy && DateTime.UtcNow < deadline)
            Thread.Sleep(100);
    }

    static string SetJsonDouble(string json, string key, double value)
    {
        var search = $"\"{key}\":";
        var idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return json;

        var start = idx + search.Length;
        var end = start;
        while (end < json.Length && json[end] != ',' && json[end] != '}')
            end++;

        return json.Substring(0, start)
             + value.ToString("F2", CultureInfo.InvariantCulture)
             + json.Substring(end);
    }

#endif

    static double TryParseDouble(string s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    static int Error(string message)
    {
        Console.Error.WriteLine($"[AIVoice1Bridge] {message}");
        return 1;
    }
}
