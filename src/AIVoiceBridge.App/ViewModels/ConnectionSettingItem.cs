using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIVoiceBridge.App.ViewModels;

/// <summary>
/// プラグイン接続設定の1項目を表すバインド可能な ViewModel。
/// PluginSettingsWindow の ItemsControl にバインドされる。
/// </summary>
public sealed class ConnectionSettingItem : INotifyPropertyChanged
{
    public string Key         { get; }
    public string Label       { get; }
    public bool   IsPassword  { get; }
    public string Placeholder { get; }

    private string _value;
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
            ValueChanged?.Invoke(Key, value);
        }
    }

    /// <summary>Value が変更されたときに発火。引数: (key, newValue)。</summary>
    public event Action<string, string>? ValueChanged;

    public ConnectionSettingItem(
        string key,
        string label,
        string currentValue,
        bool   isPassword  = false,
        string? placeholder = null)
    {
        Key         = key;
        Label       = label;
        _value      = currentValue;
        IsPassword  = isPassword;
        Placeholder = placeholder ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
