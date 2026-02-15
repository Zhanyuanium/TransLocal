using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace local_translate_provider;

public static class Program
{
    private const string SingleInstanceMutexName = "LocalTranslateProvider_SingleInstance";
    private const string WindowTitleForActivation = "Local Translate Provider";

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(false, SingleInstanceMutexName);
        if (!mutex.WaitOne(0))
        {
            if (args.Length == 0)
            {
                ActivateExistingWindow();
            }
            else
            {
                CliRunner.InitConsole();
                Console.Error.WriteLine("Already running.");
                Environment.Exit(1);
            }
            return;
        }

        if (args.Length == 0)
        {
            XamlCheckProcessRequirements();
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        else
        {
            CliRunner.InitConsole();
            Environment.Exit(CliRunner.Run(args));
        }
    }

    private static void ActivateExistingWindow()
    {
        var hwnd = FindWindow(null, WindowTitleForActivation);
        if (hwnd != IntPtr.Zero)
            SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
