using System.Windows;

namespace VoiSpeBridge.App.Views;

public partial class PluginSettingsWindow : Window
{
    public PluginSettingsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
