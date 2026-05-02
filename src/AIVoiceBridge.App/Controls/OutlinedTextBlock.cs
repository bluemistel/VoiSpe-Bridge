using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VoiSpeBridge.App.Controls;

/// <summary>
/// FormattedText → BuildGeometry でテキストをパスとして描画し、
/// 任意の太さのアウトライン（縁取り）を実現するカスタム FrameworkElement。
/// WPF 標準の TextBlock には stroke 描画がないため、このコントロールで代替する。
/// </summary>
public sealed class OutlinedTextBlock : FrameworkElement
{
    // ──── 依存関係プロパティ ────

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnFormattingChanged));

    public static readonly DependencyProperty FontFamilyNameProperty =
        DependencyProperty.Register(nameof(FontFamilyName), typeof(string), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata("メイリオ",
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnFormattingChanged));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(64.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnFormattingChanged));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(Brushes.White,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(Brushes.Black,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(6.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender));

    // ──── CLR ラッパー ────

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string FontFamilyName
    {
        get => (string)GetValue(FontFamilyNameProperty);
        set => SetValue(FontFamilyNameProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    // ──── ジオメトリ キャッシュ ────

    private Geometry? _textGeometry;

    private static void OnFormattingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((OutlinedTextBlock)d)._textGeometry = null;

    private Geometry BuildTextGeometry()
    {
        var text = Text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            return Geometry.Empty;

        // ビジュアルツリーに追加されていれば実際の DPI を使用
        double ppd = PresentationSource.FromVisual(this) is { } src
            ? src.CompositionTarget?.TransformToDevice.M11 ?? 1.0
            : 1.0;

        var typeface = new Typeface(
            new FontFamily(FontFamilyName ?? "メイリオ"),
            FontStyles.Normal,
            FontWeights.Bold,
            FontStretches.Normal);

        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            Brushes.Black,
            ppd);

        return ft.BuildGeometry(new Point(0, 0));
    }

    // ──── MeasureOverride ────

    protected override Size MeasureOverride(Size availableSize)
    {
        _textGeometry ??= BuildTextGeometry();

        if (_textGeometry.Bounds.IsEmpty)
            return new Size(StrokeThickness, StrokeThickness);

        var b = _textGeometry.Bounds;
        return new Size(
            Math.Max(0, b.Width  + StrokeThickness),
            Math.Max(0, b.Height + StrokeThickness));
    }

    // ──── OnRender ────

    protected override void OnRender(DrawingContext dc)
    {
        // レンダー時は DPI が確定しているのでキャッシュを再生成
        _textGeometry = BuildTextGeometry();

        if (_textGeometry.Bounds.IsEmpty) return;

        var b    = _textGeometry.Bounds;
        var half = StrokeThickness / 2.0;

        // stroke のはみ出しを防ぐため (half, half) だけオフセット
        dc.PushTransform(new TranslateTransform(-b.Left + half, -b.Top + half));

        // 縁取り（先に描く）
        if (StrokeThickness > 0 && StrokeBrush != null)
        {
            var pen = new Pen(StrokeBrush, StrokeThickness)
            {
                LineJoin     = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round,
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, _textGeometry);
        }

        // 塗り（後に描いて縁取りの上に重ねる）
        dc.DrawGeometry(FillBrush, null, _textGeometry);

        dc.Pop();
    }
}
