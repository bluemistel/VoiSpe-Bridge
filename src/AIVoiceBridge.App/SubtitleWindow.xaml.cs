using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VoiSpeBridge.App.ViewModels;

namespace VoiSpeBridge.App;

public partial class SubtitleWindow : Window
{
    public SubtitleWindow(SubtitleViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // 保存済みのウィンドウサイズを復元
        Width  = vm.WindowWidth;
        Height = vm.WindowHeight;

        // リサイズのたびに ViewModel へ反映（設定保存に使用）
        SizeChanged += (_, _) =>
        {
            vm.WindowWidth  = ActualWidth;
            vm.WindowHeight = ActualHeight;
        };
    }

    // ── ドラッグ移動 ──────────────────────────────────

    /// <summary>左上グリップのドラッグでウィンドウを移動。</summary>
    private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>字幕テキスト上のドラッグでもウィンドウを移動できる。</summary>
    private void OnTextMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // ── コンテキストメニュー ──────────────────────────

    /// <summary>コンテキストメニューが開く直前に「最前面に固定」のチェック状態を同期する。</summary>
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu is { } menu)
        {
            foreach (var item in menu.Items)
            {
                if (item is MenuItem mi && mi.IsCheckable)
                    mi.IsChecked = Topmost;
            }
        }
    }

    /// <summary>「字幕ウィンドウを非表示」: Dispose せず非表示にする（次回 Show() できるように）。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e)
        => Hide();

    /// <summary>「最前面に固定」トグル。</summary>
    private void OnTopmostToggle(object sender, RoutedEventArgs e)
        => Topmost = !Topmost;

    // ── システムの閉じる操作を非表示に変換 ─────────────

    /// <summary>Alt+F4 などのシステム閉じる操作でも非表示にするだけ。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
