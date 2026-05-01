using System;
using System.IO;
using System.Text;

// CeVIO AI は System.Runtime.Remoting を使用するため .NET Framework 4.8 で動作させる必要がある。
// このブリッジは AIVoiceBridge.Plugin.CeVIOAI.dll(.NET 8) からプロセスとして起動され、
// CeVIO AI との IPC を代行する。

#if USE_REAL_CEVIO_API
using CeVIO.Talk.RemoteService2;
#endif

namespace AIVoiceBridge.CeVIOBridge;

static class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
            return Error("引数が必要です。--list-casts / --list-emotions / --speak");

#if !USE_REAL_CEVIO_API
        return Error("CeVIO.Talk.RemoteService2.dll が見つかりません。" +
            "src\\Plugins\\AIVoiceBridge.Plugin.CeVIOAI\\lib\\ に配置してリビルドしてください。");
#else
        return args[0] switch
        {
            "--list-casts"    => ListCasts(),
            "--list-emotions" => ListEmotions(args),
            "--speak"         => Speak(args),
            _                 => Error($"不明なコマンド: {args[0]}")
        };
#endif
    }

#if USE_REAL_CEVIO_API
    static int ListCasts()
    {
        if (!EnsureHostStarted(out var errorCode)) return errorCode;

        var casts = TalkerAgent2.AvailableCasts;
        if (casts == null || casts.Length == 0)
            return Error("利用可能なキャストがありません。CeVIO AI のボイスをインストールしてください。");

        foreach (var c in casts)
            Console.WriteLine(c);
        return 0;
    }

    // --list-emotions <cast>
    // 出力フォーマット（タブ区切り、1行1感情）:
    //   感情名\t現在値(0-100)
    static int ListEmotions(string[] args)
    {
        if (args.Length < 2)
            return Error("--list-emotions の引数が不足しています。Usage: --list-emotions <cast>");

        var cast = args[1];
        if (!EnsureHostStarted(out var errorCode)) return errorCode;

        var talker = new Talker2 { Cast = cast };

        var components = talker.Components;
        if (components == null || components.Count == 0)
        {
            // 感情なし（正常終了でリストなし）
            return 0;
        }

        for (int i = 0; i < components.Count; i++)
        {
            var comp = components[i];
            Console.WriteLine($"{comp.Name}\t{comp.Value}");
        }
        return 0;
    }

    // --speak <textfile> <wavfile> <cast> <speed> <tone> <volume> <tonescale> [EmotionName=Value ...]
    static int Speak(string[] args)
    {
        if (args.Length < 8)
            return Error("--speak の引数が不足しています。");

        var textFile  = args[1];
        var wavFile   = args[2];
        var cast      = args[3];
        var speed     = TryParseUint(args[4], 50);
        var tone      = TryParseUint(args[5], 50);
        var volume    = TryParseUint(args[6], 100);
        var toneScale = TryParseUint(args[7], 100);

        if (!File.Exists(textFile))
            return Error($"テキストファイルが見つかりません: {textFile}");

        var text = File.ReadAllText(textFile, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
            return Error("テキストが空です。");

        if (!EnsureHostStarted(out var errorCode)) return errorCode;

        var talker = new Talker2
        {
            Cast      = cast,
            Speed     = speed,
            Tone      = tone,
            Volume    = volume,
            ToneScale = toneScale,
        };

        // 感情引数を適用（インデックス 8 以降: "EmotionName=Value"）
        if (args.Length > 8 && talker.Components != null)
        {
            for (int i = 8; i < args.Length; i++)
            {
                var parts = args[i].Split(new[] { '=' }, 2);
                if (parts.Length != 2) continue;
                var emotionName = parts[0];
                if (!uint.TryParse(parts[1], out var emotionValue)) continue;

                for (int j = 0; j < talker.Components.Count; j++)
                {
                    if (talker.Components[j].Name == emotionName)
                    {
                        talker.Components[j].Value = Math.Min(emotionValue, 100u);
                        break;
                    }
                }
            }
        }

        var success = talker.OutputWaveToFile(text, wavFile);
        if (!success)
            return Error("OutputWaveToFile が失敗しました。キャスト名とテキストを確認してください。");

        return 0;
    }

    static bool EnsureHostStarted(out int errorCode)
    {
        errorCode = 0;
        try
        {
            if (ServiceControl2.IsHostStarted) return true;

            var result = ServiceControl2.StartHost(false);
            switch (result)
            {
                case HostStartResult.Succeeded:
                case HostStartResult.AlreadyStarted:
                    return true;
                case HostStartResult.FileNotFound:
                case HostStartResult.NotRegistered:
                    errorCode = Error("CeVIO AI がインストールされていません。");
                    return false;
                default:
                    errorCode = Error($"CeVIO AI の起動に失敗しました ({result})。");
                    return false;
            }
        }
        catch (Exception ex)
        {
            errorCode = Error($"CeVIO AI ホスト起動中に例外: {ex.Message}");
            return false;
        }
    }
#endif

    static uint TryParseUint(string s, uint fallback)
        => uint.TryParse(s, out var v) ? v : fallback;

    static int Error(string message)
    {
        Console.Error.WriteLine($"[CeVIOBridge] {message}");
        return 1;
    }
}
