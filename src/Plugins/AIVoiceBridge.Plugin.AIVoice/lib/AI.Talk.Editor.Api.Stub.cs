// =============================================================================
// AI.Talk.Editor.Api スタブ（A.I.VOICE v1 用）
// =============================================================================
// 実DLL: C:\Program Files\AI\AIVoice\AIVoiceEditor\AI.Talk.Editor.Api.dll
// ビルド時は lib\ にコピーするか、HintPath を直接参照してください。
//
// このスタブは実DLLがない環境でコンパイルを通すためのものです。
// 実際のメソッドシグネチャは PowerShell リフレクションで確認済み:
//   Initialize(string hostName)
//   GetAvailableHostNames() -> string[]
//   StartHost() / TerminateHost()
//   Connect() / Disconnect()
//   Play() / Stop()
//   SaveAudioToFile(string path)
//   GetPlayTime() -> long  (ms)
//   Text, VoiceNames, VoicePresetNames, CurrentVoicePresetName プロパティ
//   Status: HostStatus  (NotRunning/NotConnected/Idle/Busy)
// =============================================================================

using System;

#pragma warning disable CS1591

namespace AI.Talk.Editor.Api
{
    public enum HostStatus { NotRunning, NotConnected, Idle, Busy }
    public enum TextEditMode { Text, List }

    public sealed class TtsControl
    {
        public bool IsInitialized { get; private set; }
        public string Version { get; private set; } = string.Empty;
        public HostStatus Status { get; private set; } = HostStatus.NotRunning;

        /// <summary>エディタのプロセス名（ホスト名）。Initialize() で指定したもの。</summary>
        public string MasterControl { get; private set; } = string.Empty;

        /// <summary>テキストエリアのテキスト</summary>
        public string Text { get; set; } = string.Empty;
        public int TextSelectionStart { get; set; }
        public int TextSelectionLength { get; set; }
        public TextEditMode TextEditMode { get; set; }

        public string[] VoiceNames { get; private set; } = [];
        public string[] VoicePresetNames { get; private set; } = [];
        public string CurrentVoicePresetName { get; set; } = string.Empty;

        // --- 接続管理 ---

        public string[] GetAvailableHostNames()
            => throw new NotImplementedException("実際の AI.Talk.Editor.Api.dll を使用してください。");

        /// <param name="hostName">GetAvailableHostNames() で取得したホスト名</param>
        public void Initialize(string hostName)
            => throw new NotImplementedException("実際の AI.Talk.Editor.Api.dll を使用してください。");

        /// <summary>A.I.VOICE Editor が起動していない場合に起動する</summary>
        public void StartHost()
            => throw new NotImplementedException();

        public void TerminateHost()
            => throw new NotImplementedException();

        public void Connect()
            => throw new NotImplementedException();

        public void Disconnect() { }

        // --- 再生 ---

        public void Play()
            => throw new NotImplementedException();

        public void Stop() { }

        /// <summary>テキストを WAV ファイルとして保存する</summary>
        public void SaveAudioToFile(string filePath)
            => throw new NotImplementedException();

        /// <summary>現在の音声の長さをミリ秒で返す</summary>
        public long GetPlayTime()
            => throw new NotImplementedException();

        // --- リスト操作 ---

        public int GetListCount() => throw new NotImplementedException();
        public void AddListItem(string voicePreset, string sentence)
            => throw new NotImplementedException();
        public void InsertListItem(string voicePreset, string sentence)
            => throw new NotImplementedException();
        public void RemoveListItem() => throw new NotImplementedException();
        public void ClearListItems() => throw new NotImplementedException();
        public string GetListVoicePreset() => throw new NotImplementedException();
        public void SetListVoicePreset(string preset) => throw new NotImplementedException();
        public string GetListSentence() => throw new NotImplementedException();
        public void SetListSentence(string sentence, bool updatePhoneme) => throw new NotImplementedException();

        // --- ボイスプリセット ---

        public string GetVoicePreset(string presetName) => throw new NotImplementedException();
        public void SetVoicePreset(string presetJson) => throw new NotImplementedException();
        public void AddVoicePreset(string presetJson) => throw new NotImplementedException();
        public void ReloadVoicePresets() => throw new NotImplementedException();
        public void ReloadPhraseDictionary() => throw new NotImplementedException();
        public void ReloadWordDictionary() => throw new NotImplementedException();
        public void ReloadSymbolDictionary() => throw new NotImplementedException();
    }
}
