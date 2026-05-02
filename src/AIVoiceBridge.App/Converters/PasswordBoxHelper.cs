using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VoiSpeBridge.App.Converters;

/// <summary>
/// WPF の PasswordBox は Password プロパティが依存関係プロパティではないため
/// 通常の Binding が使えない。この添付プロパティを使って双方向バインディングを実現する。
///
/// 使い方:
///   conv:PasswordBoxHelper.BoundPassword="{Binding Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
///
/// 実装メモ:
///   DataTemplate 内の PasswordBox では WPF のバインディングエンジンが
///   DependencyProperty の変更を自動でソースに伝搬しないケースがある。
///   OnPasswordBoxChanged で SetBoundPassword した後に UpdateSource() を明示的に
///   呼ぶことで確実にソースを更新する。
/// </summary>
public static class PasswordBoxHelper
{
    // ---- 添付プロパティ ----

    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(string.Empty, OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject d)
        => (string)d.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject d, string value)
        => d.SetValue(BoundPasswordProperty, value);

    // ViewModel → PasswordBox（バッキングフィールドが変わったとき）
    private static void OnBoundPasswordChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        box.PasswordChanged -= OnPasswordBoxChanged;  // 二重登録を防ぐ
        var newValue = (string)e.NewValue ?? string.Empty;
        if (box.Password != newValue)
            box.Password = newValue;
        box.PasswordChanged += OnPasswordBoxChanged;
    }

    // PasswordBox → ViewModel（ユーザーが入力したとき）
    private static void OnPasswordBoxChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;

        // 1. 添付プロパティを更新（ViewModel → View 方向のバインディング用）
        SetBoundPassword(box, box.Password);

        // 2. バインディングソースを明示的に更新する。
        //    DataTemplate 内では WPF バインディングエンジンが DependencyProperty の変更を
        //    自動でソースに伝搬しない場合があるため、UpdateSource() で確実に反映させる。
        BindingOperations.GetBindingExpression(box, BoundPasswordProperty)?.UpdateSource();
    }
}
