using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VoiSpeBridge.App.Services;

public sealed class AppSettings
{
    public string? SelectedPluginName { get; set; }
    public double VoiceTriggerDb { get; set; } = -35.0;
    public double NoiseGateDb { get; set; } = -50.0;
    public int SilenceDurationMs { get; set; } = 800;
    public string SelectedModelName  { get; set; } = "LargeV3Turbo";
    /// <summary>音声認識エンジン名（"Whisper" または "Browser"）。</summary>
    public string SelectedEngineName { get; set; } = "Whisper";
    public double Speed { get; set; } = 1.0;
    public double Volume { get; set; } = 1.0;
    public double Pitch { get; set; } = 1.0;
    public double Intonation { get; set; } = 1.0;
    public string? SelectedOutputDeviceName { get; set; }
    public bool UseGpu { get; set; } = true;

    /// <summary>
    /// プラグイン固有の接続設定。キー: プラグイン名 → (設定キー → 値)。
    /// IPluginWithConnectionSettings を実装するプラグインの設定を永続化する。
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> PluginConnectionSettings { get; set; } = [];

    // ---- 配信用字幕ウィンドウ設定 ----

    public string SubtitleFontFamily     { get; set; } = "メイリオ";
    public double SubtitleFontSize       { get; set; } = 72.0;
    /// <summary>ARGB hex 文字列（例: "#FFFFFFFF"）。</summary>
    public string SubtitleFontColorHex   { get; set; } = "#FFFFFFFF";
    /// <summary>ARGB hex 文字列（例: "#FF000000"）。</summary>
    public string SubtitleStrokeColorHex { get; set; } = "#FF000000";
    public double SubtitleStrokeThickness { get; set; } = 6.0;
    public double SubtitleWindowWidth    { get; set; } = 900.0;
    public double SubtitleWindowHeight   { get; set; } = 180.0;

    // ---- 声量プリセット自動切り替え ----

    /// <summary>声量プリセット自動切り替え機能の有効フラグ。</summary>
    public bool   VolumePresetEnabled     { get; set; } = false;
    /// <summary>通常声量時に使用するキャスト名。</summary>
    public string NormalVolumeCastName    { get; set; } = "";
    /// <summary>音量大（叫び声等）時に使用するキャスト名。</summary>
    public string LoudVolumeCastName      { get; set; } = "";
    /// <summary>
    /// この dB 値以上の発話を「音量大」と判定するしきい値。
    /// 発話バッファ全体の平均 RMS を dB 変換した値と比較する。
    /// </summary>
    public double LoudVolumeThresholdDb   { get; set; } = -20.0;

    // ---- 音量大キャスト専用の音声パラメータ ----

    public double LoudCastSpeed      { get; set; } = 1.0;
    public double LoudCastVolume     { get; set; } = 1.0;
    public double LoudCastPitch      { get; set; } = 1.0;
    public double LoudCastIntonation { get; set; } = 1.0;

    // ---- 誤検出フィルタ ----

    /// <summary>
    /// 発話アクティブ区間（VoiceTriggerDb 超過中）の最小時間（ms）。
    /// この時間未満の発話は擦れ音等として破棄する。
    /// </summary>
    public int MinActiveSpeechMs       { get; set; } = 200;
    /// <summary>
    /// 認識テキストの最小文字数（スペース除外）。
    /// この文字数未満の認識結果は破棄する。
    /// </summary>
    public int MinRecognizedTextLength { get; set; } = 1;

    // ---- テーマ ----

    /// <summary>ダークモードを使用するか。</summary>
    public bool IsDarkMode { get; set; } = false;
}

public sealed class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiSpeBridge", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch { }
    }
}
