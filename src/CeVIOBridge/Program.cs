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
            return Error("引数が必要です。--list-casts または --speak");

#if !USE_REAL_CEVIO_API
        return Error("CeVIO.Talk.RemoteService2.dll が見つかりません。" +
            "src\\Plugins\\AIVoiceBridge.Plugin.CeVIOAI\\lib\\ に配置してリビルドしてください。");
#else
        return args[0] switch
        {
            "--list-casts" => ListCasts(),
            "--speak"      => Speak(args),
            _              => Error($"不明なコマンド: {args[0]}")
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

    // --speak <textfile> <wavfile> <cast> <speed> <tone> <volume> <tonescale>
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
