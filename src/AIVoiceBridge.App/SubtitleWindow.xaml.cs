using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using VoiSpeBridge.App.ViewModels;

namespace VoiSpeBridge.App;

public partial class SubtitleWindow : Window
{
    public SubtitleWindow(SubtitleViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    // ドラッグハンドルでウィンドウを移動
    private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // ✕ ボタン：Dispose せず非表示にする（次回また Show() できるように）
    private void OnCloseClick(object sender, RoutedEventArgs e)
        => Hide();

    // システムの閉じるボタン（Alt+F4 など）でも非表示にするだけ
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
