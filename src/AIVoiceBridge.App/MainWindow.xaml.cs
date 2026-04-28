using System.Windows;
using AIVoiceBridge.App.ViewModels;

namespace AIVoiceBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Whisper モデルの初期化（ダウンロードを含むため非同期）
        await _vm.InitializeRecognitionAsync();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }
}
