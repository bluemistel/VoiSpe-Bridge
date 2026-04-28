using System;
using System.IO;
using System.Text.Json;

namespace AIVoiceBridge.App.Services;

public sealed class AppSettings
{
    public string? SelectedPluginName { get; set; }
    public double VoiceTriggerDb { get; set; } = -35.0;
    public double NoiseGateDb { get; set; } = -50.0;
    public int SilenceDurationMs { get; set; } = 800;
    public string SelectedModelName { get; set; } = "Small";
    public double Speed { get; set; } = 1.0;
    public double Volume { get; set; } = 1.0;
    public double Pitch { get; set; } = 1.0;
    public double Intonation { get; set; } = 1.0;
    public string? SelectedOutputDeviceName { get; set; }
    public bool UseGpu { get; set; } = true;
}

public sealed class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AIVoiceBridge", "settings.json");

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
