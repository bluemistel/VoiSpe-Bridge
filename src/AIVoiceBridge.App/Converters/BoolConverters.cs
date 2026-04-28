using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

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
