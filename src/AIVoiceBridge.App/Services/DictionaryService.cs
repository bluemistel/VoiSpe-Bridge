using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VoiSpeBridge.App.Services;

public sealed class DictionaryPreset
{
    public string Name { get; set; } = "";
    public string InitialPrompt { get; set; } = "";
    /// <summary>Key: correct form, Value: list of incorrect transcription variants</summary>
    public Dictionary<string, List<string>> Dictionary { get; set; } = new();

    public DictionaryPreset Clone() => new()
    {
        Name = Name,
        InitialPrompt = InitialPrompt,
        Dictionary = Dictionary.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
    };
}

internal sealed class PresetsFile
{
    public string ActivePresetName { get; set; } = "デフォルト";
    public List<DictionaryPreset> Presets { get; set; } = [];
}

/// <summary>
/// Manages named presets, each containing an initial_prompt and a post-correction dictionary.
/// Thread-safe for Apply(): the replacement array is replaced atomically via volatile write.
/// </summary>
public sealed class DictionaryService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiSpeBridge", "dictionary_presets.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private PresetsFile _data = CreateDefault();

    // Sorted (from→to) pairs; replaced atomically so Apply() is lock-free
    private volatile (string From, string To)[] _replacements = [];

    public IReadOnlyList<DictionaryPreset> Presets => _data.Presets;
    public DictionaryPreset? ActivePreset { get; private set; }

    public event EventHandler? ActivePresetChanged;

    // ---- Load / Save ----

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<PresetsFile>(json);
                if (loaded?.Presets.Count > 0)
                    _data = loaded;
            }
        }
        catch { }
        SetActivePreset(_data.ActivePresetName);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, JsonOpts));
        }
        catch { }
    }

    // ---- Preset management ----

    public void SetActivePreset(string name)
    {
        ActivePreset = _data.Presets.FirstOrDefault(p => p.Name == name)
                    ?? _data.Presets[0];
        _data.ActivePresetName = ActivePreset.Name;
        RebuildReplacements();
        ActivePresetChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Inserts a fully configured preset (call after the editor dialog is confirmed).
    /// Deduplicates the name if needed.
    /// </summary>
    public void InsertPreset(DictionaryPreset preset)
    {
        var name = preset.Name.Trim();
        if (_data.Presets.Any(p => p.Name == name))
            preset.Name = $"{name}_{_data.Presets.Count}";

        _data.Presets.Add(preset);
        Save();
    }

    public void DeletePreset(string name)
    {
        if (_data.Presets.Count <= 1) return;
        _data.Presets.RemoveAll(p => p.Name == name);
        if (ActivePreset?.Name == name)
            SetActivePreset(_data.Presets[0].Name);
        Save();
    }

    /// <summary>
    /// Replaces or inserts a preset by name.
    /// If the active preset is updated, rebuilds the replacement array and fires the event.
    /// </summary>
    public void UpdatePreset(DictionaryPreset updated)
    {
        var idx = _data.Presets.FindIndex(p => p.Name == updated.Name);
        if (idx < 0) return;
        _data.Presets[idx] = updated;

        if (ActivePreset?.Name == updated.Name)
        {
            ActivePreset = updated;
            RebuildReplacements();
            ActivePresetChanged?.Invoke(this, EventArgs.Empty);
        }
        Save();
    }

    // ---- Text transformation ----

    /// <summary>
    /// Applies the active preset's dictionary to the transcribed text.
    /// Called on the recognition callback thread; lock-free via volatile array swap.
    /// </summary>
    public string Apply(string text)
    {
        foreach (var (from, to) in _replacements)
            text = text.Replace(from, to, StringComparison.Ordinal);
        return text;
    }

    // ---- Helpers ----

    private void RebuildReplacements()
    {
        if (ActivePreset is null)
        {
            _replacements = [];
            return;
        }

        _replacements = ActivePreset.Dictionary
            .SelectMany(kv =>
                kv.Value
                  .Where(v => !string.IsNullOrWhiteSpace(v))
                  .Select(v => (From: v.Trim(), To: kv.Key)))
            .OrderByDescending(x => x.From.Length)
            .ToArray();
    }

    private static PresetsFile CreateDefault() => new()
    {
        ActivePresetName = "デフォルト",
        Presets = [new DictionaryPreset { Name = "デフォルト" }],
    };
}
