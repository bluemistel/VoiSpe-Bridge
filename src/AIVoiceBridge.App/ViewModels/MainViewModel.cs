using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AIVoiceBridge.App.Services;
using AIVoiceBridge.Core;

namespace AIVoiceBridge.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PluginManager _pluginManager;
    private readonly SpeechRecognitionService _recognition;
    private readonly AudioOutputService _audioOutput;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _speakCts;

    // ---- ステータス ----

    private string _statusText = "初期化中...";
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    private bool _isModelLoading;
    public bool IsModelLoading
    {
        get => _isModelLoading;
        set
        {
            if (Set(ref _isModelLoading, value))
                OnPropertyChanged(nameof(CanToggleListen));
        }
    }

    // ---- マイク操作 ----

    private bool _isListening;
    public bool IsListening
    {
        get => _isListening;
        set
        {
            if (Set(ref _isListening, value))
            {
                OnPropertyChanged(nameof(ListenButtonLabel));
                OnPropertyChanged(nameof(CanToggleListen));
            }
        }
    }

    private bool _isSpeaking;
    public bool IsSpeaking
    {
        get => _isSpeaking;
        set
        {
            if (Set(ref _isSpeaking, value))
                OnPropertyChanged(nameof(CanToggleListen));
        }
    }

    public string ListenButtonLabel => IsListening ? "■ 認識停止" : "● 認識開始";
    public bool CanToggleListen => SelectedPlugin != null && !IsModelLoading;

    // ---- 音声認識テキスト ----

    private string _recognizedText = string.Empty;
    public string RecognizedText
    {
        get => _recognizedText;
        set => Set(ref _recognizedText, value);
    }

    // ---- 音声レベルメーター ----

    private double _audioLevel;
    public double AudioLevel
    {
        get => _audioLevel;
        set => Set(ref _audioLevel, value);
    }

    private string _audioLevelDbText = "-- dB";
    public string AudioLevelDbText
    {
        get => _audioLevelDbText;
        set => Set(ref _audioLevelDbText, value);
    }

    // ---- 音声認識設定 ----

    private double _noiseGateDb = -50.0;
    public double NoiseGateDb
    {
        get => _noiseGateDb;
        set
        {
            if (Set(ref _noiseGateDb, Math.Round(value, 0)))
                _recognition.NoiseGateDb = (float)_noiseGateDb;
        }
    }

    private double _voiceTriggerDb = -35.0;
    public double VoiceTriggerDb
    {
        get => _voiceTriggerDb;
        set
        {
            if (Set(ref _voiceTriggerDb, Math.Round(value, 0)))
                _recognition.VoiceTriggerDb = (float)_voiceTriggerDb;
        }
    }

    private int _silenceDurationMs = 800;
    public int SilenceDurationMs
    {
        get => _silenceDurationMs;
        set
        {
            if (Set(ref _silenceDurationMs, value))
                _recognition.SilenceDurationMs = value;
        }
    }

    private RecognitionModelType _selectedModel = RecognitionModelType.Small;
    public RecognitionModelType SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (Set(ref _selectedModel, value))
                _ = ReloadModelAsync();
        }
    }

    private bool _useGpu = true;
    public bool UseGpu
    {
        get => _useGpu;
        set
        {
            if (Set(ref _useGpu, value))
            {
                _recognition.UseGpu = value;
                _ = ReloadModelAsync();
            }
        }
    }

    public IReadOnlyList<RecognitionModelType> AvailableModels { get; } =
        Enum.GetValues<RecognitionModelType>().ToArray();

    // ---- プラグイン ----

    public ObservableCollection<IVoiceSynthesizerPlugin> AvailablePlugins { get; } = [];

    private IVoiceSynthesizerPlugin? _selectedPlugin;
    public IVoiceSynthesizerPlugin? SelectedPlugin
    {
        get => _selectedPlugin;
        set
        {
            if (Set(ref _selectedPlugin, value))
            {
                _ = OnPluginChangedAsync();
                OnPropertyChanged(nameof(CanToggleListen));
                OnPropertyChanged(nameof(IsCastSelectable));
            }
        }
    }

    // ---- キャスト ----

    public ObservableCollection<CastInfo> AvailableCasts { get; } = [];

    private CastInfo? _selectedCast;
    public CastInfo? SelectedCast
    {
        get => _selectedCast;
        set
        {
            if (Set(ref _selectedCast, value) && _selectedPlugin != null && value != null)
                _selectedPlugin.CurrentCast = value.Name;
        }
    }

    public bool IsCastSelectable => AvailableCasts.Count > 1 ||
        (AvailableCasts.Count == 1 && !AvailableCasts[0].Name.StartsWith("（"));

    // ---- 音声パラメータ ----

    private double _speed = 1.0;
    public double Speed
    {
        get => _speed;
        set { if (Set(ref _speed, Math.Round(value, 2))) SyncOptions(); }
    }

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set { if (Set(ref _volume, Math.Round(value, 2))) SyncOptions(); }
    }

    private double _pitch = 1.0;
    public double Pitch
    {
        get => _pitch;
        set { if (Set(ref _pitch, Math.Round(value, 2))) SyncOptions(); }
    }

    private double _intonation = 1.0;
    public double Intonation
    {
        get => _intonation;
        set { if (Set(ref _intonation, Math.Round(value, 2))) SyncOptions(); }
    }

    // ---- 出力デバイス ----

    public List<AudioDeviceInfo> OutputDevices { get; } = [];

    private AudioDeviceInfo? _selectedOutputDevice;
    public AudioDeviceInfo? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set
        {
            if (Set(ref _selectedOutputDevice, value) && value != null)
                _audioOutput.SetOutputDevice(value.Index);
        }
    }

    // ---- 発話履歴 ----

    public ObservableCollection<HistoryEntry> History { get; } = [];

    // ---- 手動入力 ----

    private string _manualInput = string.Empty;
    public string ManualInput
    {
        get => _manualInput;
        set => Set(ref _manualInput, value);
    }

    // ---- コマンド ----

    public RelayCommand ToggleListenCommand { get; }
    public RelayCommand<string> SpeakTextCommand { get; }
    public RelayCommand StopSpeakingCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }

    // ===== 初期化 =====

    public MainViewModel()
    {
        _pluginManager = new PluginManager();
        _recognition = new SpeechRecognitionService();
        _audioOutput = new AudioOutputService();
        _settingsService = new SettingsService();

        ToggleListenCommand = new RelayCommand(_ => ToggleListen(), _ => CanToggleListen);
        SpeakTextCommand = new RelayCommand<string>(
            async text => await SpeakAsync(text ?? ManualInput, clearRecognized: false));
        StopSpeakingCommand = new RelayCommand(_ => StopSpeaking(), _ => IsSpeaking);
        ClearHistoryCommand = new RelayCommand(_ => History.Clear());

        LoadOutputDevices();
        _pluginManager.LoadPlugins();
        ApplySettings(_settingsService.Load());
    }

    private void ApplySettings(AppSettings s)
    {
        // 認識設定（バッキングフィールドを直接設定してPropertyChangedを後で一括通知）
        _voiceTriggerDb = s.VoiceTriggerDb;
        _noiseGateDb = s.NoiseGateDb;
        _silenceDurationMs = s.SilenceDurationMs;

        // 音声パラメータ
        _speed = s.Speed;
        _volume = s.Volume;
        _pitch = s.Pitch;
        _intonation = s.Intonation;

        // Whisperモデル（ReloadModelAsyncをトリガーしないよう直接代入）
        if (Enum.TryParse<RecognitionModelType>(s.SelectedModelName, out var model))
            _selectedModel = model;

        // GPU設定（ReloadModelAsyncをトリガーしないよう直接代入）
        _useGpu = s.UseGpu;
        _recognition.UseGpu = s.UseGpu;

        // 出力デバイス
        if (s.SelectedOutputDeviceName != null)
        {
            var device = OutputDevices.FirstOrDefault(d => d.Name == s.SelectedOutputDeviceName);
            if (device != null)
            {
                _selectedOutputDevice = device;
                _audioOutput.SetOutputDevice(device.Index);
            }
        }

        // プラグイン（設定のプラグイン名で初回接続）
        PopulatePlugins(s.SelectedPluginName);
    }

    private void SaveSettings()
    {
        _settingsService.Save(new AppSettings
        {
            SelectedPluginName = _selectedPlugin?.Name,
            VoiceTriggerDb = _voiceTriggerDb,
            NoiseGateDb = _noiseGateDb,
            SilenceDurationMs = _silenceDurationMs,
            SelectedModelName = _selectedModel.ToString(),
            UseGpu = _useGpu,
            Speed = _speed,
            Volume = _volume,
            Pitch = _pitch,
            Intonation = _intonation,
            SelectedOutputDeviceName = _selectedOutputDevice?.Name,
        });
    }

    /// <summary>Window.Loaded から呼ぶ。Whisper モデルの初期化（ダウンロードあり）。</summary>
    public async Task InitializeRecognitionAsync()
    {
        _recognition.TextRecognized += OnTextRecognized;
        _recognition.AudioLevelChanged += (_, db) =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                AudioLevel = Math.Clamp((db + 60.0) / 60.0, 0.0, 1.0);
                AudioLevelDbText = db < -59f ? "-- dB" : $"{db:F0} dB";
            });
        _recognition.StatusChanged += (_, msg) =>
            Application.Current?.Dispatcher.Invoke(() => StatusText = msg);

        IsModelLoading = true;
        try
        {
            await _recognition.InitializeAsync(_selectedModel);

            // 保存済み設定を認識サービスへ反映
            _recognition.NoiseGateDb = (float)_noiseGateDb;
            _recognition.VoiceTriggerDb = (float)_voiceTriggerDb;
            _recognition.SilenceDurationMs = _silenceDurationMs;
        }
        catch (Exception ex)
        {
            StatusText = $"音声認識の初期化に失敗しました: {ex.Message}";
        }
        finally
        {
            IsModelLoading = false;
        }
    }

    private async Task ReloadModelAsync()
    {
        if (IsListening)
        {
            _recognition.StopListening();
            IsListening = false;
        }

        IsModelLoading = true;
        try
        {
            await _recognition.InitializeAsync(_selectedModel);
        }
        catch (Exception ex)
        {
            StatusText = $"モデル変更に失敗しました: {ex.Message}";
        }
        finally
        {
            IsModelLoading = false;
        }
    }

    private void LoadOutputDevices()
    {
        foreach (var d in AudioOutputService.GetOutputDevices())
            OutputDevices.Add(d);
        SelectedOutputDevice = OutputDevices.FirstOrDefault();
    }

    private void PopulatePlugins(string? preferredName)
    {
        foreach (var p in _pluginManager.Plugins)
            AvailablePlugins.Add(p);

        // 設定に保存されたプラグイン名を優先、なければ先頭
        var preferred = preferredName != null
            ? AvailablePlugins.FirstOrDefault(p => p.Name == preferredName)
            : null;

        // バッキングフィールドを直接セットして接続を1回だけ発火
        _selectedPlugin = preferred ?? AvailablePlugins.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedPlugin));
        OnPropertyChanged(nameof(CanToggleListen));
        OnPropertyChanged(nameof(IsCastSelectable));
        _ = OnPluginChangedAsync();
    }

    // ===== イベントハンドラ =====

    private void OnTextRecognized(object? sender, string text)
    {
        Application.Current?.Dispatcher.Invoke(() => RecognizedText = text);
        _ = SpeakAsync(text, clearRecognized: true);
    }

    private async Task OnPluginChangedAsync()
    {
        if (_selectedPlugin == null) return;

        StatusText = $"{_selectedPlugin.Name} に接続中...";
        try
        {
            await _selectedPlugin.ConnectAsync();
            var casts = _selectedPlugin.GetAvailableCasts();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                AvailableCasts.Clear();
                foreach (var c in casts)
                    AvailableCasts.Add(c);
                SelectedCast = AvailableCasts.FirstOrDefault();
                OnPropertyChanged(nameof(IsCastSelectable));
            });

            SyncOptions();
            StatusText = $"{_selectedPlugin.Name} 接続済み";
        }
        catch (Exception ex)
        {
            StatusText = $"接続失敗: {ex.Message}";
        }
    }

    // ===== 操作 =====

    private void ToggleListen()
    {
        if (IsListening)
        {
            _recognition.StopListening();
            IsListening = false;
            StatusText = "認識停止";
        }
        else
        {
            _recognition.StartListening();
            IsListening = true;
            StatusText = "認識中...（話しかけてください）";
        }
    }

    public async Task SpeakAsync(string text, bool clearRecognized = false)
    {
        if (string.IsNullOrWhiteSpace(text) || _selectedPlugin == null) return;

        if (!_selectedPlugin.IsConnected)
        {
            StatusText = $"{_selectedPlugin.Name} が接続されていません。プラグインを再接続してください。";
            return;
        }

        _speakCts?.Cancel();
        _speakCts = new CancellationTokenSource();
        var ct = _speakCts.Token;

        IsSpeaking = true;
        StatusText = "発声中...";

        // A.I.VOICE2 への送信と同時に認識テキストをクリア（発声完了を待たない）
        if (clearRecognized)
            Application.Current?.Dispatcher.Invoke(() => RecognizedText = string.Empty);

        try
        {
            var wavData = await _selectedPlugin.SynthesizeAsync(text);

            if (ct.IsCancellationRequested) return;

            if (wavData != null)
                await _audioOutput.PlayWavAsync(wavData);
            else
                await _selectedPlugin.SpeakAsync(text);

            if (!ct.IsCancellationRequested)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    History.Insert(0, new HistoryEntry(DateTime.Now, text));
                    if (History.Count > 200) History.RemoveAt(200);
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"発声エラー [{ex.GetType().Name}]: {ex.Message}";
            return;
        }
        finally
        {
            IsSpeaking = false;
        }

        if (!ct.IsCancellationRequested)
            StatusText = IsListening ? "認識中...（話しかけてください）" : "待機中";
    }

    private void StopSpeaking()
    {
        _speakCts?.Cancel();
        _audioOutput.Stop();
    }

    private void SyncOptions()
    {
        if (_selectedPlugin == null) return;
        _selectedPlugin.Options = new SynthesisOptions
        {
            Speed = _speed,
            Volume = _volume,
            Pitch = _pitch,
            Intonation = _intonation,
        };
    }

    // ===== INotifyPropertyChanged =====

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

    public void Dispose()
    {
        SaveSettings();
        _recognition.Dispose();
        _audioOutput.Dispose();
        _pluginManager.DisposeAll();
    }
}

public record HistoryEntry(DateTime Time, string Text)
{
    public string DisplayTime => Time.ToString("HH:mm:ss");
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? p) => canExecute?.Invoke(p) ?? true;
    public void Execute(object? p) => execute(p);
}

public sealed class RelayCommand<T>(Func<T?, Task> execute) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => execute(p is T t ? t : default);
}
