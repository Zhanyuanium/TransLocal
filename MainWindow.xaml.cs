using local_translate_provider.Models;
using local_translate_provider.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace local_translate_provider;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(420, 520));
        LoadSettings();
        BackendCombo.SelectionChanged += (_, _) => UpdateBackendVisibility();
        StrategyCombo.SelectionChanged += (_, _) => UpdateStrategyVisibility();
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            AppWindow.Hide();
        };
    }

    private void LoadSettings()
    {
        var s = App.Settings;
        PortBox.Text = s.Port.ToString();
        DeepLCheck.IsChecked = s.EnableDeepLEndpoint;
        GoogleCheck.IsChecked = s.EnableGoogleEndpoint;
        ApiKeyBox.Text = s.ApiKey ?? "";
        MinimizeTrayCheck.IsChecked = s.MinimizeToTrayOnStartup;
        BackendCombo.SelectedIndex = s.TranslationBackend == TranslationBackend.PhiSilica ? 0 : 1;
        ModelAliasBox.Text = s.FoundryModelAlias;
        StrategyCombo.SelectedIndex = s.ExecutionStrategy switch
        {
            FoundryExecutionStrategy.PowerSaving => 0,
            FoundryExecutionStrategy.HighPerformance => 1,
            _ => 2
        };
        DeviceCombo.SelectedIndex = s.ManualDeviceType switch
        {
            FoundryDeviceType.CPU => 0,
            FoundryDeviceType.GPU => 1,
            FoundryDeviceType.NPU => 2,
            _ => 3
        };
        UpdateBackendVisibility();
        UpdateStrategyVisibility();
        _ = RefreshStatusAsync();
    }

    private void UpdateBackendVisibility()
    {
        var isFoundry = BackendCombo.SelectedIndex == 1;
        FoundryPanel.Visibility = isFoundry ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStrategyVisibility()
    {
        var isManual = StrategyCombo.SelectedIndex == 2;
        ManualDevicePanel.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
    }

    private async System.Threading.Tasks.Task RefreshStatusAsync()
    {
        try
        {
            var status = await App.TranslationService.GetStatusAsync();
            StatusText.Text = status.IsReady ? $"状态: {status.Message}" : $"状态: {status.Message}";
            if (!string.IsNullOrEmpty(status.Detail))
                StatusText.Text += $"\n{status.Detail}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"状态: {ex.Message}";
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
        {
            StatusText.Text = "端口无效";
            return;
        }

        var s = App.Settings;
        s.Port = port;
        s.EnableDeepLEndpoint = DeepLCheck.IsChecked == true;
        s.EnableGoogleEndpoint = GoogleCheck.IsChecked == true;
        s.ApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Text) ? null : ApiKeyBox.Text.Trim();
        s.MinimizeToTrayOnStartup = MinimizeTrayCheck.IsChecked == true;
        s.TranslationBackend = BackendCombo.SelectedIndex == 0 ? TranslationBackend.PhiSilica : TranslationBackend.FoundryLocal;
        s.FoundryModelAlias = ModelAliasBox.Text.Trim();
        s.ExecutionStrategy = StrategyCombo.SelectedIndex switch
        {
            0 => FoundryExecutionStrategy.PowerSaving,
            1 => FoundryExecutionStrategy.HighPerformance,
            _ => FoundryExecutionStrategy.Manual
        };
        s.ManualDeviceType = DeviceCombo.SelectedIndex switch
        {
            0 => FoundryDeviceType.CPU,
            1 => FoundryDeviceType.GPU,
            2 => FoundryDeviceType.NPU,
            _ => FoundryDeviceType.WebGPU
        };

        await SettingsService.SaveAsync(s);
        App.TranslationService.UpdateSettings(s);
        App.HttpServer.Restart();
        StatusText.Text = $"已保存。服务运行于 http://localhost:{s.Port}";
        await RefreshStatusAsync();
    }
}
