using System;
using System.Windows;

namespace AIVoiceBridge.App;

public partial class App : Application
{
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

            // それ以外は通常のクラッシュログとして扱う（ダイアログは出さない）
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
