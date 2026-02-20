using TransLocal;
using TransLocal.Models;
using TransLocal.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.Resources;
using Windows.System;

namespace TransLocal.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly ResourceLoader ResLoader = ResourceLoader.GetForViewIndependentUse();

    private string _tag = "General";

    public SettingsPage()
    {
        InitializeComponent();
        BackendCombo.SelectionChanged += (_, _) => { UpdateBackendVisibility(); UpdateStrategyVisibility(); };
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

    private async void OpenProxySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("ms-settings:network-proxy"));
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
        ApiProxyHintText.Text = string.Format(ResLoader.GetString("ApiProxyHintFormat"), s.Port);
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
            _ => 0
        };
        UpdateBackendVisibility();
        UpdateStrategyVisibility();
        DebugLogSwitch.IsOn = s.DebugLogEnabled;
        _ = RefreshStatusAsync();
    }

    private async void ExportCertButton_Click(object sender, RoutedEventArgs e)
    {
        var window = App.MainWindow;
        if (window == null) return;
        var picker = new FileSavePicker(window.AppWindow.Id)
        {
            SuggestedFileName = "TransLocal-ca.cer",
            FileTypeChoices = { { "Certificate", new List<string> { ".cer" } } },
            DefaultFileExtension = ".cer"
        };
        var result = await picker.PickSaveFileAsync();
        if (result != null)
        {
            App.CertManager.ExportCaToFile(result.Path);
        }
    }

    private async void InstallCertButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.CertManager.TryInstallCaToCurrentUser())
        {
            var dialog = new ContentDialog
            {
                Title = ResLoader.GetString("InstallCertSuccessTitle"),
                Content = ResLoader.GetString("InstallCertSuccessContent"),
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        else
        {
            var dialog = new ContentDialog
            {
                Title = ResLoader.GetString("InstallCertFailTitle"),
                Content = ResLoader.GetString("InstallCertFailContent"),
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private async void UnloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        UnloadModelButton.IsEnabled = false;
        try
        {
            await App.TranslationService.UnloadModelAsync();
            StatusText.Text = ResLoader.GetString("UnloadModelSuccess");
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"{ResLoader.GetString("StatusPrefix")}{ex.Message}";
        }
        finally
        {
            UnloadModelButton.IsEnabled = true;
        }
    }

    private async void DebugLogSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        var s = App.Settings;
        s.DebugLogEnabled = DebugLogSwitch.IsOn;
        DebugLog.IsEnabled = s.DebugLogEnabled;
        await SettingsService.SaveAsync(s);
    }

    private void UpdateBackendVisibility()
    {
        var isFoundry = BackendCombo.SelectedIndex == 1;
        FoundryPanel.Visibility = isFoundry ? Visibility.Visible : Visibility.Collapsed;
        ManualDeviceBorder.Visibility = isFoundry ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStrategyVisibility()
    {
        var isManual = StrategyCombo.SelectedIndex == 2;
        DeviceComboContainer.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
    }

    private async System.Threading.Tasks.Task RefreshStatusAsync()
    {
        try
        {
            var status = await App.TranslationService.GetStatusAsync();
            var prefix = ResLoader.GetString("StatusPrefix");
            var msg = status.MessageFormatArgs != null && status.MessageFormatArgs.Length > 0
                ? string.Format(ResLoader.GetString(status.MessageResourceKey), status.MessageFormatArgs)
                : ResLoader.GetString(status.MessageResourceKey);
            StatusText.Text = $"{prefix}{msg}";
            var detail = status.DetailResourceKey != null
                ? ResLoader.GetString(status.DetailResourceKey)
                : status.DetailRaw;
            if (!string.IsNullOrEmpty(detail))
                StatusText.Text += $"\n{detail}";
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
            App.Settings.FoundryModelAlias = "phi-4-mini";
            App.Settings.ExecutionStrategy = FoundryExecutionStrategy.HighPerformance;
            App.Settings.ManualDeviceType = FoundryDeviceType.CPU;
            App.Settings.DebugLogEnabled = false;
            DebugLog.IsEnabled = false;
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
            _ => FoundryDeviceType.CPU
        };

        await SettingsService.SaveAsync(s);
        App.TranslationService.UpdateSettings(s);
        App.HttpServer.Restart();
        StatusText.Text = string.Format(ResLoader.GetString("SavedMessage"), s.Port);
        await RefreshStatusAsync();
    }
}
