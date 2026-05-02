using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VoiSpeBridge.App.Services;
using VoiSpeBridge.Core;

namespace VoiSpeBridge.App.ViewModels;

/// <summary>音声認識エンジンの選択肢。</summary>
public enum RecognitionEngine
{
    /// <summary>Whisper（ローカル実行、GPU/CPU）</summary>
    Whisper,
    /// <summary>ブラウザ音声認識（Google クラウド・GPU 不使用）</summary>
    Browser,
    /// <summary>ReazonSpeech（日本語特化・ローカル実行・GPU 不使用・ストリーミング）</summary>
    ReazonSpeech,
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PluginManager _pluginManager;
    private readonly SpeechRecognitionService _recognition;
    private readonly BrowserSpeechRecognitionService _browserRecognition = new();
    private readonly ReazonSpeechRecognitionService _reazonRecognition = new();
    private readonly AudioOutputService _audioOutput;
    private readonly SettingsService _settingsService;
    private readonly DictionaryService _dictionaryService;
    private CancellationTokenSource? _speakCts;

    /// <summary>
    /// Set by the View to open the preset editor dialog.
    /// Receives a cloned preset; returns true when the user confirmed.
    /// </summary>
    public Func<DictionaryPreset, bool>? ShowPresetEditor { get; set; }

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
    public bool CanToggleListen => SelectedPlugin != null && !IsModelLoading
        && (_selectedEngine == RecognitionEngine.Whisper
            || (_selectedEngine == RecognitionEngine.Browser  && _browserRecognition.IsInitialized)
            || (_selectedEngine == RecognitionEngine.ReazonSpeech && _reazonRecognition.IsModelReady));

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

    // ---- 認識エンジン ----

    public IReadOnlyList<RecognitionEngine> AvailableEngines { get; } =
        Enum.GetValues<RecognitionEngine>().ToArray();

    /// <summary>ブラウザ認識サービスへの参照（MainWindow から InitializeAsync に使用）。</summary>
    public BrowserSpeechRecognitionService BrowserRecognition => _browserRecognition;

    /// <summary>ReazonSpeech 認識サービスへの参照（初期化状況の公開）。</summary>
    public ReazonSpeechRecognitionService ReazonRecognition => _reazonRecognition;

    private RecognitionEngine _selectedEngine = RecognitionEngine.Whisper;
    public RecognitionEngine SelectedEngine
    {
        get => _selectedEngine;
        set
        {
            if (Set(ref _selectedEngine, value))
            {
                OnPropertyChanged(nameof(IsWhisperEngine));
                OnPropertyChanged(nameof(IsBrowserEngine));
                OnPropertyChanged(nameof(IsReazonSpeechEngine));
                OnPropertyChanged(nameof(ShowVadSettings));
                OnPropertyChanged(nameof(CanToggleListen));
                _ = OnEngineChangedAsync();
            }
        }
    }

    /// <summary>現在 Whisper エンジンが選択されているか（モデル選択・GPU 設定の表示切替に使用）。</summary>
    public bool IsWhisperEngine     => _selectedEngine == RecognitionEngine.Whisper;

    /// <summary>現在ブラウザエンジンが選択されているか（UI の表示切替に使用）。</summary>
    public bool IsBrowserEngine     => _selectedEngine == RecognitionEngine.Browser;

    /// <summary>現在 ReazonSpeech エンジンが選択されているか（UI の表示切替に使用）。</summary>
    public bool IsReazonSpeechEngine => _selectedEngine == RecognitionEngine.ReazonSpeech;

    /// <summary>
    /// VAD スライダー（発話検出レベル・ノイズゲート・無音判定）を表示するか。
    /// Whisper と ReazonSpeech はどちらも同じ VAD パターンを使用するため共通で表示する。
    /// </summary>
    public bool ShowVadSettings =>
        _selectedEngine == RecognitionEngine.Whisper
        || _selectedEngine == RecognitionEngine.ReazonSpeech;

    // ---- 音声認識設定 ----

    private double _noiseGateDb = -50.0;
    public double NoiseGateDb
    {
        get => _noiseGateDb;
        set
        {
            if (Set(ref _noiseGateDb, Math.Round(value, 0)))
            {
                _recognition.NoiseGateDb      = (float)_noiseGateDb;
                _reazonRecognition.NoiseGateDb = (float)_noiseGateDb;
            }
        }
    }

    private double _voiceTriggerDb = -35.0;
    public double VoiceTriggerDb
    {
        get => _voiceTriggerDb;
        set
        {
            if (Set(ref _voiceTriggerDb, Math.Round(value, 0)))
            {
                _recognition.VoiceTriggerDb      = (float)_voiceTriggerDb;
                _reazonRecognition.VoiceTriggerDb = (float)_voiceTriggerDb;
            }
        }
    }

    private int _silenceDurationMs = 800;
    public int SilenceDurationMs
    {
        get => _silenceDurationMs;
        set
        {
            if (Set(ref _silenceDurationMs, value))
            {
                _recognition.SilenceDurationMs      = value;
                _reazonRecognition.SilenceDurationMs = value;
            }
        }
    }

    private RecognitionModelType _selectedModel = RecognitionModelType.LargeV3Turbo;
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
                LoadPluginConnectionSettings();
                _ = OnPluginChangedAsync();
                OnPropertyChanged(nameof(CanToggleListen));
                OnPropertyChanged(nameof(IsCastSelectable));
                OnPropertyChanged(nameof(PluginCastLabel));
            }
        }
    }

    // ---- プラグイン接続設定（IPluginWithConnectionSettings 実装プラグイン向け） ----

    public ObservableCollection<ConnectionSettingItem> PluginConnectionSettings { get; } = [];

    private bool _hasPluginConnectionSettings;
    public bool HasPluginConnectionSettings
    {
        get => _hasPluginConnectionSettings;
        private set => Set(ref _hasPluginConnectionSettings, value);
    }

    public RelayCommand ReconnectPluginCommand { get; private set; } = null!;

    // ---- 感情パラメータ（IPluginWithEmotions 実装プラグイン向け） ----

    public ObservableCollection<EmotionItem> EmotionParameters { get; } = [];

    private bool _hasEmotionParameters;
    public bool HasEmotionParameters
    {
        get => _hasEmotionParameters;
        private set => Set(ref _hasEmotionParameters, value);
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
            {
                _selectedPlugin.CurrentCast = value.Name;
                OnPropertyChanged(nameof(PluginCastLabel));
                _ = RefreshEmotionParametersAsync();
            }
        }
    }

    public bool IsCastSelectable => AvailableCasts.Count > 1 ||
        (AvailableCasts.Count == 1 && !AvailableCasts[0].Name.StartsWith("（"));

    /// <summary>Compact label shown in the header bar.</summary>
    public string PluginCastLabel =>
        _selectedPlugin == null ? "（プラグイン未選択）" :
        IsCastSelectable && _selectedCast != null
            ? $"{_selectedPlugin.Name}  /  {_selectedCast.Name}"
            : _selectedPlugin.Name;

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

    // ---- 辞書プリセット ----

    public ObservableCollection<DictionaryPreset> DictionaryPresets { get; } = [];

    private DictionaryPreset? _selectedDictionaryPreset;
    public DictionaryPreset? SelectedDictionaryPreset
    {
        get => _selectedDictionaryPreset;
        set
        {
            if (Set(ref _selectedDictionaryPreset, value) && value != null)
                ApplyDictionaryPreset(value.Name);
        }
    }

    // ---- コマンド ----

    // ---- 配信用字幕 ----

    /// <summary>字幕ウィンドウの表示設定。SubtitleWindow の DataContext として使用。</summary>
    public SubtitleViewModel Subtitle { get; } = new SubtitleViewModel();

    public RelayCommand ToggleSubtitleWindowCommand { get; private set; } = null!;

    /// <summary>Set by the View to toggle (show/hide) the subtitle window.</summary>
    public Action? ShowSubtitleWindow { get; set; }

    // ---- コマンド ----

    public RelayCommand ToggleListenCommand { get; }
    public RelayCommand<string> SpeakTextCommand { get; }
    public RelayCommand StopSpeakingCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }
    public RelayCommand AddPresetCommand { get; }
    public RelayCommand EditPresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }
    public RelayCommand OpenPluginSettingsCommand { get; }

    /// <summary>Set by the View to open (or focus) the plugin settings window.</summary>
    public Action? ShowPluginSettings { get; set; }

    // ===== 初期化 =====

    public MainViewModel()
    {
        _pluginManager = new PluginManager();
        _recognition = new SpeechRecognitionService();
        _audioOutput = new AudioOutputService();
        _settingsService = new SettingsService();
        _dictionaryService = new DictionaryService();

        ToggleListenCommand = new RelayCommand(_ => ToggleListen(), _ => CanToggleListen);
        SpeakTextCommand = new RelayCommand<string>(
            async text => await SpeakAsync(text ?? ManualInput, clearRecognized: false));
        StopSpeakingCommand = new RelayCommand(_ => StopSpeaking(), _ => IsSpeaking);
        ClearHistoryCommand = new RelayCommand(_ => History.Clear());
        AddPresetCommand = new RelayCommand(_ => AddNewPreset());
        EditPresetCommand = new RelayCommand(_ => EditSelectedPreset(),
            _ => SelectedDictionaryPreset != null);
        DeletePresetCommand = new RelayCommand(_ => DeleteSelectedPreset(),
            _ => SelectedDictionaryPreset != null && DictionaryPresets.Count > 1);
        OpenPluginSettingsCommand  = new RelayCommand(_ => ShowPluginSettings?.Invoke());
        ReconnectPluginCommand     = new RelayCommand(_ => _ = OnPluginChangedAsync());
        ToggleSubtitleWindowCommand = new RelayCommand(_ => ShowSubtitleWindow?.Invoke());

        LoadOutputDevices();
        _pluginManager.LoadPlugins();
        _dictionaryService.Load();
        PopulateDictionaryPresets();
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

        // 認識エンジン（イベントをトリガーしないよう直接代入）
        if (Enum.TryParse<RecognitionEngine>(s.SelectedEngineName, out var engine))
            _selectedEngine = engine;
        OnPropertyChanged(nameof(IsWhisperEngine));
        OnPropertyChanged(nameof(IsBrowserEngine));
        OnPropertyChanged(nameof(IsReazonSpeechEngine));
        OnPropertyChanged(nameof(ShowVadSettings));

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

        // プラグイン固有接続設定を復元してから接続（ConnectAsync より前に設定が必要）
        RestoreAllPluginConnectionSettings(s.PluginConnectionSettings);

        // プラグイン（設定のプラグイン名で初回接続）
        PopulatePlugins(s.SelectedPluginName);
    }

    private void SaveSettings()
    {
        _settingsService.Save(new AppSettings
        {
            SelectedPluginName       = _selectedPlugin?.Name,
            VoiceTriggerDb           = _voiceTriggerDb,
            NoiseGateDb              = _noiseGateDb,
            SilenceDurationMs        = _silenceDurationMs,
            SelectedModelName        = _selectedModel.ToString(),
            SelectedEngineName       = _selectedEngine.ToString(),
            UseGpu                   = _useGpu,
            Speed                    = _speed,
            Volume                   = _volume,
            Pitch                    = _pitch,
            Intonation               = _intonation,
            SelectedOutputDeviceName = _selectedOutputDevice?.Name,
            PluginConnectionSettings = CollectAllPluginConnectionSettings(),
        });
    }

    /// <summary>Window.Loaded から呼ぶ。エンジンに応じて Whisper / Browser / ReazonSpeech を初期化する。</summary>
    public async Task InitializeRecognitionAsync()
    {
        // ---- 各サービスのイベント購読（エンジンが一致するときのみ転送）----
        _recognition.TextRecognized += (s, text) =>
        {
            if (_selectedEngine == RecognitionEngine.Whisper) OnTextRecognized(s, text);
        };
        _browserRecognition.TextRecognized += (s, text) =>
        {
            if (_selectedEngine == RecognitionEngine.Browser) OnTextRecognized(s, text);
        };
        _reazonRecognition.TextRecognized += (s, text) =>
        {
            if (_selectedEngine == RecognitionEngine.ReazonSpeech) OnTextRecognized(s, text);
        };

        // Use BeginInvoke (fire-and-forget) so background threads never block on the
        // dispatcher. Dispatcher.Invoke would deadlock if called after the dispatcher
        // starts shutting down, preventing the process from exiting.
        _recognition.AudioLevelChanged += (_, db) =>
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                AudioLevel = Math.Clamp((db + 60.0) / 60.0, 0.0, 1.0);
                AudioLevelDbText = db < -59f ? "-- dB" : $"{db:F0} dB";
            });
        // ReazonSpeech も独自のレベルメーターを持つ
        _reazonRecognition.AudioLevelChanged += (_, db) =>
        {
            if (_selectedEngine == RecognitionEngine.ReazonSpeech)
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    AudioLevel = Math.Clamp((db + 60.0) / 60.0, 0.0, 1.0);
                    AudioLevelDbText = db < -59f ? "-- dB" : $"{db:F0} dB";
                });
        };

        _recognition.StatusChanged += (_, msg) =>
            Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
        // ブラウザ / ReazonSpeech のステータスは選択中エンジンのときのみ転送
        // （他エンジンに切り替えた後も StatusChanged が発火し続けるため必須）
        _browserRecognition.StatusChanged += (_, msg) =>
        {
            if (_selectedEngine == RecognitionEngine.Browser)
                Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
        };
        _reazonRecognition.StatusChanged += (_, msg) =>
        {
            if (_selectedEngine == RecognitionEngine.ReazonSpeech)
                Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
        };

        // ---- エンジン別初期化 ----
        if (_selectedEngine == RecognitionEngine.Browser)
        {
            // ブラウザモード: Whisper モデル不要、NAudio レベル監視のみ
            await _recognition.InitializeLevelMonitorAsync();
        }
        else if (_selectedEngine == RecognitionEngine.ReazonSpeech)
        {
            // ReazonSpeech モード: モデルダウンロード→ロード（初回は数分かかります）
            IsModelLoading = true;
            try
            {
                await _reazonRecognition.InitializeAsync();
                // 保存済み VAD 設定を反映
                _reazonRecognition.NoiseGateDb       = (float)_noiseGateDb;
                _reazonRecognition.VoiceTriggerDb    = (float)_voiceTriggerDb;
                _reazonRecognition.SilenceDurationMs = _silenceDurationMs;
            }
            catch (Exception ex)
            {
                StatusText = $"ReazonSpeech 初期化失敗: {ex.Message}";
            }
            finally
            {
                IsModelLoading = false;
                OnPropertyChanged(nameof(CanToggleListen));
            }
        }
        else
        {
            // Whisper モード: モデルをダウンロード・ロード
            _recognition.InitialPrompt = _dictionaryService.ActivePreset?.InitialPrompt;

            IsModelLoading = true;
            try
            {
                await _recognition.InitializeAsync(_selectedModel);

                // 保存済み設定を認識サービスへ反映
                _recognition.NoiseGateDb       = (float)_noiseGateDb;
                _recognition.VoiceTriggerDb    = (float)_voiceTriggerDb;
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
    }

    private async Task ReloadModelAsync()
    {
        // Whisper 以外のエンジン選択中はモデルリロード不要
        if (_selectedEngine != RecognitionEngine.Whisper) return;

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

    /// <summary>エンジン切り替え時：認識を停止し、新しいエンジンを初期化する。</summary>
    private async Task OnEngineChangedAsync()
    {
        // 全サービスを停止（旧エンジンが何であれ安全に停止できる）
        if (IsListening)
        {
            _reazonRecognition.StopListening();
            _browserRecognition.StopListening();
            _recognition.StopListening();
            IsListening = false;
        }

        if (_selectedEngine == RecognitionEngine.Browser)
        {
            // ブラウザモードへ切替: Whisper メモリ解放 + レベル監視のみ初期化
            await _recognition.InitializeLevelMonitorAsync();
        }
        else if (_selectedEngine == RecognitionEngine.ReazonSpeech)
        {
            // ReazonSpeech モードへ切替: モデルが未ロードなら初期化
            if (!_reazonRecognition.IsModelReady)
            {
                IsModelLoading = true;
                try
                {
                    await _reazonRecognition.InitializeAsync();
                    // 保存済み VAD 設定を反映
                    _reazonRecognition.NoiseGateDb       = (float)_noiseGateDb;
                    _reazonRecognition.VoiceTriggerDb    = (float)_voiceTriggerDb;
                    _reazonRecognition.SilenceDurationMs = _silenceDurationMs;
                }
                catch (Exception ex)
                {
                    StatusText = $"ReazonSpeech 初期化失敗: {ex.Message}";
                }
                finally
                {
                    IsModelLoading = false;
                }
            }
            else
            {
                // モデル済み（一度起動後に再選択した場合など）→ 再初期化不要
                _reazonRecognition.NoiseGateDb       = (float)_noiseGateDb;
                _reazonRecognition.VoiceTriggerDb    = (float)_voiceTriggerDb;
                _reazonRecognition.SilenceDurationMs = _silenceDurationMs;
                StatusText = "ReazonSpeech 準備完了";
            }
        }
        else
        {
            // Whisper モードへ切替: モデルをロード
            IsModelLoading = true;
            try
            {
                _recognition.InitialPrompt = _dictionaryService.ActivePreset?.InitialPrompt;
                await _recognition.InitializeAsync(_selectedModel);
                _recognition.NoiseGateDb       = (float)_noiseGateDb;
                _recognition.VoiceTriggerDb    = (float)_voiceTriggerDb;
                _recognition.SilenceDurationMs = _silenceDurationMs;
            }
            catch (Exception ex)
            {
                StatusText = $"Whisper 初期化失敗: {ex.Message}";
            }
            finally
            {
                IsModelLoading = false;
            }
        }

        OnPropertyChanged(nameof(CanToggleListen));
        SaveSettings();
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
        LoadPluginConnectionSettings();   // 接続設定を UI に反映
        _ = OnPluginChangedAsync();
    }

    private void PopulateDictionaryPresets()
    {
        DictionaryPresets.Clear();
        foreach (var p in _dictionaryService.Presets)
            DictionaryPresets.Add(p);

        _selectedDictionaryPreset = DictionaryPresets
            .FirstOrDefault(p => p.Name == _dictionaryService.ActivePreset?.Name)
            ?? DictionaryPresets.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedDictionaryPreset));
    }

    private void ApplyDictionaryPreset(string name)
    {
        _dictionaryService.SetActivePreset(name);
        _recognition.InitialPrompt = _dictionaryService.ActivePreset?.InitialPrompt;

        // Rebuild the processor with the new initial_prompt; stop listening first if needed
        if (IsListening)
        {
            _recognition.StopListening();
            IsListening = false;
            StatusText = $"プリセット「{name}」に変更しました。認識を再開してください。";
        }
        else
        {
            StatusText = $"プリセット「{name}」に変更しました。";
        }
        _ = RebuildProcessorAsync();
    }

    private async Task RebuildProcessorAsync()
    {
        IsModelLoading = true;
        try { await _recognition.InitializeAsync(_selectedModel); }
        catch (Exception ex) { StatusText = $"プロセッサ再構築に失敗: {ex.Message}"; }
        finally { IsModelLoading = false; }
    }

    private void AddNewPreset()
    {
        var blank = new DictionaryPreset { Name = $"プリセット {DictionaryPresets.Count + 1}" };
        if (ShowPresetEditor?.Invoke(blank) == true)
        {
            _dictionaryService.InsertPreset(blank);
            PopulateDictionaryPresets();
            _selectedDictionaryPreset =
                DictionaryPresets.FirstOrDefault(p => p.Name == blank.Name)
                ?? DictionaryPresets.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedDictionaryPreset));
            if (_selectedDictionaryPreset != null)
                ApplyDictionaryPreset(_selectedDictionaryPreset.Name);
        }
    }

    private void EditSelectedPreset()
    {
        if (SelectedDictionaryPreset == null) return;
        var clone = SelectedDictionaryPreset.Clone();
        if (ShowPresetEditor?.Invoke(clone) == true)
        {
            _dictionaryService.UpdatePreset(clone);

            // Refresh the list item (Name might have changed)
            PopulateDictionaryPresets();
            _selectedDictionaryPreset =
                DictionaryPresets.FirstOrDefault(p => p.Name == clone.Name)
                ?? DictionaryPresets.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedDictionaryPreset));

            // Re-apply if this is the active preset
            if (_dictionaryService.ActivePreset?.Name == clone.Name)
                ApplyDictionaryPreset(clone.Name);
        }
    }

    private void DeleteSelectedPreset()
    {
        if (SelectedDictionaryPreset == null || DictionaryPresets.Count <= 1) return;
        var name = SelectedDictionaryPreset.Name;
        _dictionaryService.DeletePreset(name);
        PopulateDictionaryPresets();
    }

    // ===== イベントハンドラ =====

    private void OnTextRecognized(object? sender, string text)
    {
        text = _dictionaryService.Apply(text);
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            RecognizedText     = text;
            Subtitle.DisplayText = text;   // 配信用字幕ウィンドウにも反映
        });
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
                OnPropertyChanged(nameof(PluginCastLabel));
            });

            SyncOptions();
            // キャスト確定後に感情パラメータを取得
            await RefreshEmotionParametersAsync();
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
            _reazonRecognition.StopListening();
            _browserRecognition.StopListening();
            _recognition.StopListening();
            IsListening = false;
            StatusText = "認識停止";
        }
        else
        {
            if (_selectedEngine == RecognitionEngine.Browser)
            {
                // NAudio は起動しない：WebView2/Chromium が独自にマイクを管理するため
                // NAudio と同時起動すると Windows がマイク競合を起こし "not-allowed" になる場合がある
                _browserRecognition.StartListening();
                IsListening = true;
                // StatusText はブラウザ側の StatusChanged イベントで更新される
            }
            else if (_selectedEngine == RecognitionEngine.ReazonSpeech)
            {
                // ReazonSpeech は独自の NAudio + SherpaOnnx で完結する
                _reazonRecognition.StartListening();
                IsListening = true;
                StatusText = "認識中...（ReazonSpeech）";
            }
            else
            {
                _recognition.StartListening();
                IsListening = true;
                StatusText = "認識中...（話しかけてください）";
            }
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
            Application.Current?.Dispatcher.BeginInvoke(() => RecognizedText = string.Empty);

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
                Application.Current?.Dispatcher.BeginInvoke(() =>
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

    // ===== プラグイン接続設定 =====

    /// <summary>
    /// 選択中プラグインの接続設定を UI 用 ObservableCollection に読み込む。
    /// プラグインが IPluginWithConnectionSettings を実装していない場合はコレクションを空にする。
    /// </summary>
    private void LoadPluginConnectionSettings()
    {
        PluginConnectionSettings.Clear();

        if (_selectedPlugin is not IPluginWithConnectionSettings settingsPlugin)
        {
            HasPluginConnectionSettings = false;
            return;
        }

        foreach (var def in settingsPlugin.ConnectionSettingDefinitions)
        {
            var item = new ConnectionSettingItem(
                def.Key,
                def.Label,
                settingsPlugin.GetConnectionSetting(def.Key) ?? def.DefaultValue,
                def.IsPassword,
                def.Placeholder);

            item.ValueChanged += (key, value) =>
            {
                settingsPlugin.SetConnectionSetting(key, value);
                SaveSettings();   // 変更を即座に永続化
            };

            PluginConnectionSettings.Add(item);
        }

        HasPluginConnectionSettings = PluginConnectionSettings.Count > 0;
    }

    /// <summary>
    /// 起動時に AppSettings から全プラグインの接続設定を復元する。
    /// ConnectAsync() より前に呼ぶこと。
    /// </summary>
    private void RestoreAllPluginConnectionSettings(
        Dictionary<string, Dictionary<string, string>> saved)
    {
        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin is not IPluginWithConnectionSettings sp) continue;
            if (!saved.TryGetValue(plugin.Name, out var dict)) continue;

            foreach (var (key, value) in dict)
                sp.SetConnectionSetting(key, value);
        }
    }

    // ===== 感情パラメータ =====

    /// <summary>
    /// 現在のプラグイン・キャストの感情パラメータを非同期で読み込み、UI コレクションを更新する。
    /// プラグインが IPluginWithEmotions を実装していない場合はコレクションを空にする。
    /// </summary>
    private async Task RefreshEmotionParametersAsync()
    {
        if (_selectedPlugin is not IPluginWithEmotions emotionPlugin)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                EmotionParameters.Clear();
                HasEmotionParameters = false;
            });
            return;
        }

        try { await emotionPlugin.RefreshEmotionsAsync(); }
        catch { /* 感情取得失敗（ボイス未対応など）は無視 */ }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            EmotionParameters.Clear();
            foreach (var ep in emotionPlugin.GetEmotions())
            {
                var item = new EmotionItem(ep.Key, ep.Label, ep.Value, ep.Min, ep.Max);
                item.ValueChanged += (key, val) => emotionPlugin.SetEmotion(key, val);
                EmotionParameters.Add(item);
            }
            HasEmotionParameters = EmotionParameters.Count > 0;
        });
    }

    /// <summary>全プラグインの接続設定を AppSettings 用の辞書に収集する。</summary>
    private Dictionary<string, Dictionary<string, string>> CollectAllPluginConnectionSettings()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin is not IPluginWithConnectionSettings sp) continue;

            var dict = new Dictionary<string, string>();
            foreach (var def in sp.ConnectionSettingDefinitions)
            {
                var value = sp.GetConnectionSetting(def.Key);
                if (value != null)
                    dict[def.Key] = value;
            }

            if (dict.Count > 0)
                result[plugin.Name] = dict;
        }

        return result;
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
        StopSpeaking();
        _speakCts?.Dispose();
        SaveSettings();
        _recognition.Dispose();
        _browserRecognition.Dispose();
        _reazonRecognition.Dispose();
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
