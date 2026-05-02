using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VoiSpeBridge.App.Services;
using VoiSpeBridge.App.ViewModels;

namespace VoiSpeBridge.App.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is not true;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is not true;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is not Visibility.Visible;
}

/// <summary>RecognitionModelType → 表示用日本語ラベルに変換するコンバーター。</summary>
[ValueConversion(typeof(RecognitionModelType), typeof(string))]
public sealed class ModelTypeToLabelConverter : IValueConverter
{
    private static readonly Dictionary<RecognitionModelType, string> Labels = new()
    {
        [RecognitionModelType.Tiny]         = "Tiny  （77MB · 最速）",
        [RecognitionModelType.Base]         = "Base  （142MB · 高速）",
        [RecognitionModelType.Small]        = "Small  （467MB · バランス）",
        [RecognitionModelType.LargeV3Turbo] = "Large v3 Turbo  （809MB · 高精度 ★推奨）",
        [RecognitionModelType.Medium]       = "Medium  （1.5GB · 最高精度）",
    };

    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is RecognitionModelType m && Labels.TryGetValue(m, out var s) ? s : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>System.Windows.Media.Color → SolidColorBrush に変換するコンバーター。</summary>
[ValueConversion(typeof(Color), typeof(SolidColorBrush))]
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is Color col ? new SolidColorBrush(col) : Brushes.Transparent;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is SolidColorBrush b ? b.Color : Colors.Transparent;
}

/// <summary>RecognitionEngine → 表示用日本語ラベルに変換するコンバーター。</summary>
[ValueConversion(typeof(RecognitionEngine), typeof(string))]
public sealed class EngineTypeToLabelConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is RecognitionEngine e ? e switch
        {
            RecognitionEngine.Whisper      => "Whisper  （ローカル・GPU/CPU）",
            RecognitionEngine.Browser      => "ブラウザ  （Google クラウド・GPU不使用）",
            RecognitionEngine.ReazonSpeech => "ReazonSpeech  （ローカル・CPU・日本語特化・ストリーミング）",
            _                              => value.ToString() ?? string.Empty,
        } : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
