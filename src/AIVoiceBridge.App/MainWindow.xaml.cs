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

        // 驟堺ｿ｡逕ｨ蟄怜ｹ輔え繧｣繝ｳ繝峨え縺ｮ陦ｨ遉ｺ蛻・崛
        _vm.ShowSubtitleWindow = ToggleSubtitleWindow;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // WebView2 繧貞・縺ｫ蛻晄悄蛹厄ｼ・hromium 繝励Ο繧ｻ繧ｹ襍ｷ蜍輔・繝槭う繧ｯ讓ｩ髯蝉ｻ倅ｸ趣ｼ・        await _vm.BrowserRecognition.InitializeAsync(BrowserWebView);
        // 髻ｳ螢ｰ隱崎ｭ倥お繝ｳ繧ｸ繝ｳ繧貞・譛溷喧・・hisper 縺ｮ蝣ｴ蜷医・繝｢繝・Ν繝ｭ繝ｼ繝会ｼ・        await _vm.InitializeRecognitionAsync();
    }

    // 笏笏笏笏 驟堺ｿ｡逕ｨ蟄怜ｹ輔え繧｣繝ｳ繝峨え 笏笏笏笏

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

    // 笏笏笏笏 繧ｫ繝ｩ繝ｼ繝斐ャ繧ｫ繝ｼ・・inForms ColorDialog・俄楳笏笏笏

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

    // 笏笏笏笏 繧ｯ繝ｪ繝ｼ繝ｳ繧｢繝・・ 笏笏笏笏

    protected override void OnClosed(System.EventArgs e)
    {
        _subtitleWin?.Close();
        _pluginSettingsWin?.Close();
        _vm.Dispose();
        base.OnClosed(e);
        // whisper.cpp 縺ｮ OpenMP/CUDA 繧ｹ繝ｬ繝・ラ繝励・繝ｫ縺ｯ whisper_free() 蠕後ｂ谿句ｭ倥＠
        // 繝励Ο繧ｻ繧ｹ繧堤函縺九＠邯壹￠繧九◆繧√［anaged 繧ｯ繝ｪ繝ｼ繝ｳ繧｢繝・・螳御ｺ・ｾ後↓蠑ｷ蛻ｶ邨ゆｺ・☆繧九・        Environment.Exit(0);
    }
}

