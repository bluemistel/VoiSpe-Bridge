using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AIVoiceBridge.App.Services;

namespace AIVoiceBridge.App.Converters;

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
