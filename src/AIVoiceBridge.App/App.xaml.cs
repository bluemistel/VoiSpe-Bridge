using System;
using System.Windows;
using System.Windows.Media;

namespace VoiSpeBridge.App;

public partial class App : Application
{
    // ──── テーマ切替 ────

    /// <summary>ライトモードのブラシ定義（R,G,B）</summary>
    private static readonly (string Key, byte R, byte G, byte B)[] LightBrushes =
    [
        ("BackgroundBrush",    0xF1, 0xF2, 0xF5),
        ("SurfaceBrush",       0xFF, 0xFF, 0xFF),
        ("Surface2Brush",      0xF6, 0xF7, 0xFA),
        ("AccentBrush",        0x3F, 0x6F, 0xB7),
        ("AccentHoverBrush",   0x34, 0x5D, 0x9C),
        ("AccentSoftBrush",    0xE9, 0xF0, 0xF9),
        ("TextPrimaryBrush",   0x2A, 0x30, 0x3B),
        ("TextSecondaryBrush", 0x4A, 0x52, 0x60),
        ("FgOnAccentBrush",    0xFF, 0xFF, 0xFF),
        ("SuccessBrush",       0x3D, 0x9A, 0x6B),
        ("DangerBrush",        0xD2, 0x60, 0x60),
        ("BorderBrush",        0xE2, 0xE5, 0xEC),
    ];

    /// <summary>ダークモードのブラシ定義（R,G,B）</summary>
    private static readonly (string Key, byte R, byte G, byte B)[] DarkBrushes =
    [
        ("BackgroundBrush",    0x2A, 0x30, 0x3B),
        ("SurfaceBrush",       0x33, 0x3A, 0x47),
        ("Surface2Brush",      0x2D, 0x33, 0x40),
        ("AccentBrush",        0x6E, 0x9B, 0xD9),
        ("AccentHoverBrush",   0x5A, 0x87, 0xC5),
        ("AccentSoftBrush",    0x1A, 0x28, 0x40),
        ("TextPrimaryBrush",   0xE2, 0xE6, 0xEC),
        ("TextSecondaryBrush", 0x9A, 0xA3, 0xB2),
        ("FgOnAccentBrush",    0xFF, 0xFF, 0xFF),
        ("SuccessBrush",       0x4D, 0xB0, 0x7E),
        ("DangerBrush",        0xE0, 0x70, 0x70),
        ("BorderBrush",        0x40, 0x47, 0x54),
    ];

    /// <summary>
    /// アプリ全体のカラーテーマを切り替える。
    /// Application.Resources のブラシを新しいオブジェクトで置き換えるため、
    /// DynamicResource バインディングが自動的に更新される。
    /// </summary>
    public static void SetTheme(bool isDark)
    {
        var res     = Current.Resources;
        var palette = isDark ? DarkBrushes : LightBrushes;

        // アプリブラシを置き換え（DynamicResource バインディングが自動追随）
        foreach (var (key, r, g, b) in palette)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            res[key] = brush;
        }

        // WPF システムカラーのオーバーライドブラシも置き換え
        // （ComboBox ドロップダウン・ListBox 選択色などに影響）
        var surface    = isDark ? Color.FromRgb(0x33, 0x3A, 0x47) : Color.FromRgb(0xFF, 0xFF, 0xFF);
        var textPri    = isDark ? Color.FromRgb(0xE2, 0xE6, 0xEC) : Color.FromRgb(0x2A, 0x30, 0x3B);
        var accent     = isDark ? Color.FromRgb(0x6E, 0x9B, 0xD9) : Color.FromRgb(0x3F, 0x6F, 0xB7);
        var acSoft     = isDark ? Color.FromRgb(0x1A, 0x28, 0x40) : Color.FromRgb(0xE9, 0xF0, 0xF9);
        var textSec    = isDark ? Color.FromRgb(0x9A, 0xA3, 0xB2) : Color.FromRgb(0x4A, 0x52, 0x60);
        var fgOnAccent = Color.FromRgb(0xFF, 0xFF, 0xFF); // 常に白

        static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        res[SystemColors.WindowBrushKey]                         = Frozen(surface);
        res[SystemColors.WindowTextBrushKey]                     = Frozen(textPri);
        res[SystemColors.ControlBrushKey]                        = Frozen(surface);
        res[SystemColors.ControlTextBrushKey]                    = Frozen(textPri);
        res[SystemColors.HighlightBrushKey]                      = Frozen(accent);
        res[SystemColors.HighlightTextBrushKey]                  = Frozen(fgOnAccent);
        res[SystemColors.InactiveSelectionHighlightBrushKey]     = Frozen(acSoft);
        res[SystemColors.InactiveSelectionHighlightTextBrushKey] = Frozen(textSec);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPFスレッドの未処理例外
        DispatcherUnhandledException += (_, args) =>
        {
            // シャットダウン時のプラグインALC解放エラーは無視する
            if (IsPluginShutdownError(args.Exception))
            {
                args.Handled = true;
                return;
            }

            MessageBox.Show(
                $"予期しないエラーが発生しました:\n{args.Exception.Message}",
                "AIVoiceBridge エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // バックグラウンドスレッドの未処理例外（シャットダウン時ファイナライザーなど）
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex && IsPluginShutdownError(ex))
                return; // 既知のシャットダウンエラーは無視
        };
    }

    // プラグインのAssemblyLoadContext解放やCOM後処理で発生する既知のシャットダウンエラー
    private static bool IsPluginShutdownError(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("AI.Talk.Editor.Api")
            || msg.Contains("operation is not legal in the current state")
            || (ex is System.IO.FileLoadException or System.IO.FileNotFoundException
                && msg.Contains("Talk"));
    }
}
