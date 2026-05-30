using System;
using System.Windows;
using System.Windows.Media;
using VoiSpeBridge.App.ViewModels;
using VoiSpeBridge.App.Views;

namespace VoiSpeBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private PluginSettingsWindow? _pluginSettingsWin;
    private SubtitleWindow?       _subtitleWin;

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

        // 配信用字幕ウィンドウの表示切替
        _vm.ShowSubtitleWindow = ToggleSubtitleWindow;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // WebView2 を先に初期化（Chromium プロセス起動・マイク権限準備）
        // WebView2 ランタイム未インストール等で失敗しても音声認識初期化はブロックしない
        try
        {
            await _vm.BrowserRecognition.InitializeAsync(BrowserWebView);
        }
        catch (Exception ex)
        {
            // ブラウザエンジンは使用不可になるが、Whisper / ReazonSpeech は影響なし
            System.Diagnostics.Debug.WriteLine(
                $"[OnLoaded] WebView2 初期化失敗（ブラウザエンジン無効）: {ex.Message}");
        }

        // 音声認識エンジンを初期化（Whisper の場合はモデルロード）
        await _vm.InitializeRecognitionAsync();
    }

    // ──── 配信用字幕ウィンドウ ────

    private void ToggleSubtitleWindow()
    {
        if (_subtitleWin is not { IsLoaded: true })
        {
            _subtitleWin = new SubtitleWindow(_vm.Subtitle);
            _subtitleWin.Show();
            return;
        }

        if (_subtitleWin.IsVisible)
            _subtitleWin.Hide();
        else
            _subtitleWin.Show();
    }

    // ──── カラーピッカー（WinForms ColorDialog）────

    private void OnPickFontColor(object sender, RoutedEventArgs e)
    {
        var c = _vm.Subtitle.FontColor;
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            Color    = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B),
            FullOpen = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var d = dlg.Color;
            _vm.Subtitle.FontColor = Color.FromArgb(d.A, d.R, d.G, d.B);
        }
    }

    private void OnPickStrokeColor(object sender, RoutedEventArgs e)
    {
        var c = _vm.Subtitle.StrokeColor;
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            Color    = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B),
            FullOpen = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var d = dlg.Color;
            _vm.Subtitle.StrokeColor = Color.FromArgb(d.A, d.R, d.G, d.B);
        }
    }

    // ──── クリーンアップ ────

    protected override void OnClosed(System.EventArgs e)
    {
        try
        {
            _subtitleWin?.Close();
            _pluginSettingsWin?.Close();
            _vm.Dispose();
            base.OnClosed(e);
        }
        catch { }
        finally
        {
            // whisper.cpp の OpenMP/CUDA スレッドプールや SherpaOnnx ネイティブスレッドは
            // Dispose 後もプロセスを生かし続けることがあるため、マネージドクリーンアップ完了後
            // に強制終了してプロセスが残り続けることを防ぐ。
            Environment.Exit(0);
        }
    }
}
