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
    private HttpTranslationServer? _httpServer;

    public static AppSettings Settings => (Current as App)!._settings;
    public static TranslationService TranslationService => (Current as App)!._translationService!;
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
        _translationService = new TranslationService(_settings);
        _httpServer = new HttpTranslationServer(_settings, _translationService);
        _httpServer.Start();

        _trayIcon = new TrayIconManager(ShowSettings, DoExit);

        if (_settings.MinimizeToTrayOnStartup)
        {
            // 延迟创建 MainWindow，首次点击托盘或 ShowSettings 时再创建
        }
        else
        {
            _window = new MainWindow(OnMainWindowClosing);
            _window.Activate();
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
        _httpServer?.Stop();
        _trayIcon?.Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
