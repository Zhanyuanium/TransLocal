using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace TransLocal;

public static class Program
{
    private const string SingleInstanceMutexName = "TransLocal_SingleInstance";

    /// <summary>
    /// 启动参数，供 App 读取。Main 中设置后由 Application.Start 回调内的 App 使用。
    /// </summary>
    internal static string[] LaunchArgs { get; private set; } = Array.Empty<string>();

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    private static readonly string DebugLogEnvVar = "TRANSLOCAL_DEBUG_LOG";

    [STAThread]
    public static void Main(string[] args)
    {
        InitDebugLog();
        if (Array.Exists(args, a => a.Equals("--debug-log", StringComparison.OrdinalIgnoreCase)))
            DebugLog.IsEnabled = true;
        var isTrayMode = args.Length > 0 && args[0].Equals("--tray", StringComparison.OrdinalIgnoreCase);

        using var mutex = new Mutex(false, SingleInstanceMutexName);
        if (!mutex.WaitOne(0))
        {
            if (isTrayMode)
            {
                for (var i = 0; i < 30; i++)
                {
                    Thread.Sleep(100);
                    if (mutex.WaitOne(0))
                        goto HaveMutex;
                }
                Environment.Exit(1);
            }
            if (args.Length == 0)
            {
                Environment.Exit(0);
            }
            CliRunner.InitConsole();
            Environment.Exit(CliRunner.Run(args));
            return;
        }
        HaveMutex:

        var showWindow = args.Length > 1 && args[1].Equals("--show-window", StringComparison.OrdinalIgnoreCase);

        if (args.Length == 0)
        {
            SpawnTrayAndExit();
            return;
        }

        if (isTrayMode)
        {
            LaunchArgs = args;
            XamlCheckProcessRequirements();
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
            return;
        }

        CliRunner.InitConsole();
        Environment.Exit(CliRunner.Run(args));
    }

    private static void SpawnTrayAndExit()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "TransLocal.exe";
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "--tray",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            Process.Start(startInfo);
        }
        catch
        {
            Environment.Exit(1);
        }
        Environment.Exit(0);
    }

    /// <summary>
    /// 是否为托盘模式启动（--tray），此时不创建主窗口。
    /// </summary>
    internal static bool IsTrayOnlyLaunch => LaunchArgs.Length > 0 &&
        LaunchArgs[0].Equals("--tray", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否启动后立即显示主窗口（--tray --show-window）。
    /// </summary>
    internal static bool ShouldShowWindowOnLaunch => LaunchArgs.Length > 1 &&
        LaunchArgs[1].Equals("--show-window", StringComparison.OrdinalIgnoreCase);

    private static void InitDebugLog()
    {
        var v = Environment.GetEnvironmentVariable(DebugLogEnvVar);
        DebugLog.IsEnabled = v is "1" ||
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
    }
}

