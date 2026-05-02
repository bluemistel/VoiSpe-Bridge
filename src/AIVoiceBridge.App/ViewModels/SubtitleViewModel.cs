using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace VoiSpeBridge.App.ViewModels;

/// <summary>
/// 配信用字幕ウィンドウの表示設定を保持する ViewModel。
/// MainViewModel から参照され、SubtitleWindow の DataContext として使用する。
/// </summary>
public sealed class SubtitleViewModel : INotifyPropertyChanged
{
    // ──── 表示テキスト ────

    private string _displayText = "";
    public string DisplayText
    {
        get => _displayText;
        set => Set(ref _displayText, value);
    }

    // ──── フォント ────

    private string _fontFamilyName = "メイリオ";
    public string FontFamilyName
    {
        get => _fontFamilyName;
        set => Set(ref _fontFamilyName, value);
    }

    private double _fontSize = 72.0;
    public double FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, value);
    }

    /// <summary>システムにインストールされているフォント一覧（名前順）。</summary>
    public IReadOnlyList<string> InstalledFonts { get; } =
        Fonts.SystemFontFamilies
             .Select(f => f.Source)
             .OrderBy(s => s, System.StringComparer.CurrentCultureIgnoreCase)
             .ToList();

    // ──── 文字色 ────

    private Color _fontColor = Colors.White;
    public Color FontColor
    {
        get => _fontColor;
        set
        {
            if (Set(ref _fontColor, value))
            {
                _fontBrush = new SolidColorBrush(value);
                _fontBrush.Freeze();
                OnPropertyChanged(nameof(FontBrush));
            }
        }
    }

    private Brush _fontBrush = Brushes.White;

    /// <summary>OutlinedTextBlock の FillBrush にバインドする。FontColor 変更時に自動更新。</summary>
    public Brush FontBrush => _fontBrush;

    // ──── 縁取り色 ────

    private Color _strokeColor = Colors.Black;
    public Color StrokeColor
    {
        get => _strokeColor;
        set
        {
            if (Set(ref _strokeColor, value))
            {
                _strokeBrush = new SolidColorBrush(value);
                _strokeBrush.Freeze();
                OnPropertyChanged(nameof(StrokeBrush));
            }
        }
    }

    private Brush _strokeBrush = Brushes.Black;

    /// <summary>OutlinedTextBlock の StrokeBrush にバインドする。StrokeColor 変更時に自動更新。</summary>
    public Brush StrokeBrush => _strokeBrush;

    // ──── 縁取りの太さ ────

    private double _strokeThickness = 6.0;
    public double StrokeThickness
    {
        get => _strokeThickness;
        set => Set(ref _strokeThickness, value);
    }

    // ──── INPC ────

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
