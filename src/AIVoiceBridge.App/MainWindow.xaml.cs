using System;
using System.Windows;
using AIVoiceBridge.App.ViewModels;
using AIVoiceBridge.App.Views;

namespace AIVoiceBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private PluginSettingsWindow? _pluginSettingsWin;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.ShowPresetEditor = preset =>
        {
            var editor = new DictionaryPresetEditorWindow(preset) { Owner = this };
            return editor.ShowDialog() == true;
        };

        _vm.ShowPluginSettings = () =>
        {
            if (_pluginSettingsWin is { IsLoaded: true })
            {
                _pluginSettingsWin.Activate();
                return;
            }
            _pluginSettingsWin = new PluginSettingsWindow
            {
                DataContext = _vm,
                Owner = this,
            };
            _pluginSettingsWin.Show();
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.InitializeRecognitionAsync();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _pluginSettingsWin?.Close();
        _vm.Dispose();
        base.OnClosed(e);
        // whisper.cpp の OpenMP/CUDA スレッドプールは whisper_free() 後も残存し
        // プロセスを生かし続けるため、managed クリーンアップ完了後に強制終了する。
        Environment.Exit(0);
    }
}
