using System.Threading;
using System.Threading.Tasks;
using local_translate_provider.Models;
using local_translate_provider.Services;
using local_translate_provider.TrayIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace local_translate_provider;

public partial class App : Application
{
    private MainWindow? _window;
    private TrayIconManager? _trayIcon;
    private AppSettings _settings = new();
    private TranslationService? _translationService;
    private CertificateManager? _certManager;
    private HttpTranslationServer? _httpServer;
    private IpcServer? _ipcServer;

    public static AppSettings Settings => (Current as App)!._settings;
    public static MainWindow? MainWindow => (Current as App)!._window;
    public static TranslationService TranslationService => (Current as App)!._translationService!;
    public static CertificateManager CertManager => (Current as App)!._certManager!;
    public static HttpTranslationServer HttpServer => (Current as App)!._httpServer!;

    public App()
    {
        InitializeComponent();
        // 关闭主窗口时不退出应用，保持托盘运行，需显式调用 Exit() 才退出
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _settings = await SettingsService.LoadAsync();
        if (_settings.DebugLogEnabled)
            DebugLog.IsEnabled = true;
        _translationService = new TranslationService(_settings);
        _certManager = new CertificateManager();
        _httpServer = new HttpTranslationServer(_settings, _translationService, _certManager);
        _httpServer.Start();

        _trayIcon = new TrayIconManager(ShowSettings, DoExit);

        var dq = DispatcherQueue.GetForCurrentThread();
        _ipcServer = new IpcServer(
            onGui: () => dq.TryEnqueue(ShowSettings),
            onQuit: () => dq.TryEnqueue(DoExit),
            onReload: () => dq.TryEnqueue(ReloadSettings),
            getStatusAsync: GetStatusAsync);
        _ipcServer.Start();

        var trayOnly = Program.IsTrayOnlyLaunch;
        var showWindow = Program.ShouldShowWindowOnLaunch;

        if (trayOnly)
        {
            if (showWindow)
                dq.TryEnqueue(ShowSettings);
        }
        else if (_settings.MinimizeToTrayOnStartup)
        {
            // 延迟创建 MainWindow，首次点击托盘或 ShowSettings 时再创建
        }
        else
        {
            _window = new MainWindow(OnMainWindowClosing);
            _window.Activate();
        }
    }

    private void ReloadSettings()
    {
        _ = ReloadSettingsAsync();
    }

    private async System.Threading.Tasks.Task ReloadSettingsAsync()
    {
        _settings = await SettingsService.LoadAsync();
        _translationService!.UpdateSettings(_settings);
        _httpServer!.Stop();
        _httpServer = new HttpTranslationServer(_settings, _translationService, _certManager!);
        _httpServer.Start();

    }

    private static async Task<string> GetStatusAsync()
    {
        try
        {
            var s = await TranslationService.GetStatusAsync().ConfigureAwait(false);
            var result = $"Backend: {Settings.TranslationBackend}\nReady: {s.IsReady}\nMessage: {s.Message}";
            if (!string.IsNullOrEmpty(s.Detail))
                result += $"\nDetail: {s.Detail}";
            return result;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private void ShowSettings()
    {
        if (_window == null)
        {
            // 打开前先回收上次关闭后可能残留的内存，缓解重复打开时的增长
            MemoryHelper.TrimWorkingSetSync();
            _window = new MainWindow(OnMainWindowClosing);
        }
        _window.AppWindow.Show();
        _window.Activate();
    }

    /// <summary>
    /// 主窗口关闭时调用，释放引用并在后台执行 GC + EmptyWorkingSet，使内存占用回归仅托盘运行的水平。
    /// </summary>
    private void OnMainWindowClosing()
    {
        _window = null;
        // 延迟调度，待窗口完全销毁后在后台线程执行内存回收
        DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, MemoryHelper.TrimWorkingSetAsync);
    }

    private void DoExit()
    {
        _ipcServer?.Stop();
        _httpServer?.Stop();
        _trayIcon?.Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
