using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIVoiceBridge.App.ViewModels;

/// <summary>
/// 感情・スタイルパラメータの1項目を表すバインド可能な ViewModel。
/// PluginSettingsWindow の感情スライダー ItemsControl にバインドされる。
/// </summary>
public sealed class EmotionItem : INotifyPropertyChanged
{
    public string Key   { get; }
    public string Label { get; }
    public double Min   { get; }
    public double Max   { get; }

    private double _value;
    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(Math.Round(value, 0), Min, Max);
            if (_value == clamped) return;
            _value = clamped;
            OnPropertyChanged();
            ValueChanged?.Invoke(Key, clamped);
        }
    }

    /// <summary>Value が変更されたときに発火。引数: (key, newValue)。</summary>
    public event Action<string, double>? ValueChanged;

    public EmotionItem(string key, string label, double currentValue, double min = 0.0, double max = 100.0)
    {
        Key    = key;
        Label  = label;
        Min    = min;
        Max    = max;
        _value = Math.Clamp(Math.Round(currentValue, 0), min, max);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
