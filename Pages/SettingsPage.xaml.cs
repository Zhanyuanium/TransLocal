using local_translate_provider.Models;
using local_translate_provider.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.Resources;

namespace local_translate_provider.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly ResourceLoader ResLoader = ResourceLoader.GetForViewIndependentUse();

    private string _tag = "General";

    public SettingsPage()
    {
        InitializeComponent();
        BackendCombo.SelectionChanged += (_, _) => UpdateBackendVisibility();
        StrategyCombo.SelectionChanged += (_, _) => UpdateStrategyVisibility();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _tag = (e.Parameter as string) ?? "General";
        ShowPanel(_tag);
        LoadSettings();
    }

    public async System.Threading.Tasks.Task SaveBeforeLeaveAsync()
    {
        if (_tag == "General")
            await SaveGeneralAsync();
        else if (_tag == "Service")
            await SaveServiceAsync();
    }

    private async System.Threading.Tasks.Task SaveGeneralAsync()
    {
        var s = App.Settings;
        s.RunAtStartup = RunAtStartupSwitch.IsOn;
        s.MinimizeToTrayOnStartup = MinimizeTraySwitch.IsOn;
        await SettingsService.SaveAsync(s);
    }

    private async System.Threading.Tasks.Task SaveServiceAsync()
    {
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
            return;
        var s = App.Settings;
        s.Port = port;
        s.EnableDeepLEndpoint = DeepLCheck.IsChecked == true;
        s.EnableGoogleEndpoint = GoogleCheck.IsChecked == true;
        s.ApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Text) ? null : ApiKeyBox.Text.Trim();
        await SettingsService.SaveAsync(s);
        App.HttpServer.Restart();
    }

    private void ShowPanel(string tag)
    {
        GeneralPanel.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        ModelPanel.Visibility = tag == "Model" ? Visibility.Visible : Visibility.Collapsed;
        ServicePanel.Visibility = tag == "Service" ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Visibility = tag == "Model" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadSettings()
    {
        var s = App.Settings;
        RunAtStartupSwitch.IsOn = s.RunAtStartup;
        MinimizeTraySwitch.IsOn = s.MinimizeToTrayOnStartup;
        PortBox.Text = s.Port.ToString();
        DeepLCheck.IsChecked = s.EnableDeepLEndpoint;
        GoogleCheck.IsChecked = s.EnableGoogleEndpoint;
        ApiKeyBox.Text = s.ApiKey ?? "";
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
        ManualDeviceBorder.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
    }

    private async System.Threading.Tasks.Task RefreshStatusAsync()
    {
        try
        {
            var status = await App.TranslationService.GetStatusAsync();
            var prefix = ResLoader.GetString("StatusPrefix");
            StatusText.Text = $"{prefix}{status.Message}";
            if (!string.IsNullOrEmpty(status.Detail))
                StatusText.Text += $"\n{status.Detail}";
        }
        catch (Exception ex)
        {
            var prefix = ResLoader.GetString("StatusPrefix");
            StatusText.Text = $"{prefix}{ex.Message}";
        }
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = ResLoader.GetString("ResetDialogTitle"),
            Content = ResLoader.GetString("ResetDialogContent"),
            PrimaryButtonText = ResLoader.GetString("ResetDialogConfirm"),
            CloseButtonText = ResLoader.GetString("ResetDialogCancel"),
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Settings.Port = 52860;
            App.Settings.EnableDeepLEndpoint = true;
            App.Settings.EnableGoogleEndpoint = true;
            App.Settings.ApiKey = null;
            App.Settings.RunAtStartup = false;
            App.Settings.MinimizeToTrayOnStartup = true;
            App.Settings.TranslationBackend = TranslationBackend.FoundryLocal;
            App.Settings.FoundryModelAlias = "phi-3.5-mini";
            App.Settings.ExecutionStrategy = FoundryExecutionStrategy.HighPerformance;
            App.Settings.ManualDeviceType = FoundryDeviceType.CPU;
            await SettingsService.SaveAsync(App.Settings);
            LoadSettings();
            App.TranslationService.UpdateSettings(App.Settings);
            App.HttpServer.Restart();
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
        {
            StatusText.Text = ResLoader.GetString("InvalidPort");
            return;
        }

        var s = App.Settings;
        s.RunAtStartup = RunAtStartupSwitch.IsOn;
        s.MinimizeToTrayOnStartup = MinimizeTraySwitch.IsOn;
        s.Port = port;
        s.EnableDeepLEndpoint = DeepLCheck.IsChecked == true;
        s.EnableGoogleEndpoint = GoogleCheck.IsChecked == true;
        s.ApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Text) ? null : ApiKeyBox.Text.Trim();
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
        StatusText.Text = string.Format(ResLoader.GetString("SavedMessage"), s.Port);
        await RefreshStatusAsync();
    }
}
