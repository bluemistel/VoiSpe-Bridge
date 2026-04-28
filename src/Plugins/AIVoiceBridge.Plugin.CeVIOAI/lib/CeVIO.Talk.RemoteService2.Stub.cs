// =============================================================================
// CeVIO.Talk.RemoteService2 スタブ（CeVIO AI 用）
// =============================================================================
// 実DLL: CeVIO AI インストールフォルダ内の CeVIO.Talk.RemoteService2.dll
//        インストール時に GAC にも登録されます。
// ビルド時は lib\ に実DLLをコピーするか、csproj の HintPath を変更してください。
//   コピー元の例: C:\Program Files\CeVIO\CeVIO AI\CeVIO.Talk.RemoteService2.dll
//
// 実DLLのリフレクションで確認したシグネチャ:
//   ServiceControl2.StartHost(bool) -> HostStartResult
//   ServiceControl2.CloseHost(HostCloseMode)
//   ServiceControl2.IsHostStarted -> bool
//   TalkerAgent2.AvailableCasts -> string[]   (static)
//   Talker2.{ Volume, Speed, Tone, Alpha, ToneScale: uint; Cast: string }
//   Talker2.OutputWaveToFile(string, string) -> bool
//   Talker2.Speak(string) -> SpeakingState2
//   Talker2.Stop() -> bool
// =============================================================================

using System;

#pragma warning disable CS1591

namespace CeVIO.Talk.RemoteService2
{
    public static class ServiceControl2
    {
        public static Version? HostVersion => throw new NotImplementedException("実際の CeVIO.Talk.RemoteService2.dll を使用してください。");
        public static bool IsHostStarted => throw new NotImplementedException("実際の CeVIO.Talk.RemoteService2.dll を使用してください。");

        /// <param name="noWait">true: 即時返却, false: 起動完了を待つ</param>
        public static HostStartResult StartHost(bool noWait)
            => throw new NotImplementedException("実際の CeVIO.Talk.RemoteService2.dll を使用してください。");

        public static void CloseHost(HostCloseMode mode = HostCloseMode.Default)
            => throw new NotImplementedException();
    }

    public enum HostStartResult
    {
        Succeeded = 0,
        AlreadyStarted = 1,
        NotRegistered = -1,
        FileNotFound = -2,
        BootingFailed = -3,
        HostError = -4,
    }

    public enum HostCloseMode
    {
        Default = 0,
        NotCancelable = 1,
        Interrupt = 2,
    }

    /// <summary>利用可能なキャスト一覧の取得はこちら（static）</summary>
    public static class TalkerAgent2
    {
        public static string[] AvailableCasts
            => throw new NotImplementedException("実際の CeVIO.Talk.RemoteService2.dll を使用してください。");
    }

    public class Talker2
    {
        /// <summary>選択中のキャスト名</summary>
        public string Cast { get; set; } = string.Empty;

        /// <summary>音量 (0-100, 標準: 100)</summary>
        public uint Volume { get; set; } = 100;

        /// <summary>話速 (0-100, 標準: 50)</summary>
        public uint Speed { get; set; } = 50;

        /// <summary>音の高さ / ピッチ (0-100, 標準: 50)</summary>
        public uint Tone { get; set; } = 50;

        /// <summary>声質 (0-100, 標準: 50)</summary>
        public uint Alpha { get; set; } = 50;

        /// <summary>抑揚 (0-100, 標準: 100)</summary>
        public uint ToneScale { get; set; } = 100;

        /// <summary>感情パラメータのコレクション</summary>
        public TalkerComponentCollection2 Components
            => throw new NotImplementedException();

        /// <summary>テキストを読み上げる（CeVIO AI の出力デバイスで再生）</summary>
        public SpeakingState2 Speak(string text)
            => throw new NotImplementedException("実際の CeVIO.Talk.RemoteService2.dll を使用してください。");

        public bool Stop()
            => throw new NotImplementedException();

        /// <summary>テキストを WAV ファイルとして出力する (48kHz 16bit mono)</summary>
        /// <returns>成功した場合 true</returns>
        public bool OutputWaveToFile(string text, string path)
            => throw new NotImplementedException("実際の CeVIO.Talk.RemoteService2.dll を使用してください。");

        /// <summary>テキストの読み上げ時間を秒単位で返す</summary>
        public double GetTextDuration(string text)
            => throw new NotImplementedException();
    }

    public class SpeakingState2
    {
        public bool IsCompleted { get; }
        public bool IsSucceeded { get; }
        public void Wait() => throw new NotImplementedException();
    }

    public class TalkerComponentCollection2
    {
        public int Count => 0;
        public TalkerComponent2 this[int index] => throw new NotImplementedException();
    }

    public class TalkerComponent2
    {
        public string Name { get; } = string.Empty;
        public string Id { get; } = string.Empty;
        public uint Value { get; set; }
    }
}
