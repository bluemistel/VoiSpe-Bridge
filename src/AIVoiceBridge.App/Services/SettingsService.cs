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
