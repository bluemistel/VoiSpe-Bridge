using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using AIVoiceBridge.App.Services;

namespace AIVoiceBridge.App.Views;

/// <summary>
/// Per-row binding model for the dictionary DataGrid.
/// </summary>
public sealed class DictionaryEntryRow : INotifyPropertyChanged
{
    private string _correctForm = "";
    private string _variantsText = "";

    /// <summary>The correct output form (e.g. "SynthesizerV")</summary>
    public string CorrectForm
    {
        get => _correctForm;
        set { _correctForm = value; OnPropertyChanged(); }
    }

    /// <summary>Comma-separated incorrect variants (e.g. "サイザーV, シンセサイザーヴィー")</summary>
    public string VariantsText
    {
        get => _variantsText;
        set { _variantsText = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class DictionaryPresetEditorWindow : Window
{
    public DictionaryPreset Preset { get; }
    public ObservableCollection<DictionaryEntryRow> Entries { get; }

    public DictionaryPresetEditorWindow(DictionaryPreset preset)
    {
        Preset = preset;

        Entries = new ObservableCollection<DictionaryEntryRow>(
            preset.Dictionary.Select(kv => new DictionaryEntryRow
            {
                CorrectForm = kv.Key,
                VariantsText = string.Join(", ", kv.Value),
            }));

        InitializeComponent();
        DataContext = this;
    }

    private void AddEntry_Click(object sender, RoutedEventArgs e)
    {
        Entries.Add(new DictionaryEntryRow());
        EntriesGrid.ScrollIntoView(Entries[^1]);
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is DictionaryEntryRow row)
            Entries.Remove(row);
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("プリセット名を入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Preset.Name = name;
        Preset.InitialPrompt = InitialPromptBox.Text.Trim();
        Preset.Dictionary.Clear();

        foreach (var row in Entries.Where(r => !string.IsNullOrWhiteSpace(r.CorrectForm)))
        {
            var variants = row.VariantsText
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries |
                             System.StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (variants.Count > 0)
                Preset.Dictionary[row.CorrectForm.Trim()] = variants;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
