using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using local_translate_provider.Models;
using local_translate_provider.Services;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace local_translate_provider;

public static class CliRunner
{
    private static bool _useFileStorage;

    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCP(uint wCodePageID);

    private const uint CpUtf8 = 65001;

    public static void InitConsole()
    {
        if (AttachConsole(AttachParentProcess))
        {
            SetConsoleOutputCP(CpUtf8);
            SetConsoleCP(CpUtf8);
            var stdout = GetStdHandle(StdOutputHandle);
            var stderr = GetStdHandle(StdErrorHandle);
            if (stdout != IntPtr.Zero && stdout != new IntPtr(-1))
            {
                var stdoutStream = new FileStream(new SafeFileHandle(stdout, true), FileAccess.Write);
                Console.SetOut(new StreamWriter(stdoutStream, Encoding.UTF8) { AutoFlush = true });
            }
            if (stderr != IntPtr.Zero && stderr != new IntPtr(-1))
            {
                var stderrStream = new FileStream(new SafeFileHandle(stderr, true), FileAccess.Write);
                Console.SetError(new StreamWriter(stderrStream, Encoding.UTF8) { AutoFlush = true });
            }
        }
        else
        {
            AllocConsole();
            SetConsoleOutputCP(CpUtf8);
            SetConsoleCP(CpUtf8);
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
    }

    public static int Run(string[] args)
    {
        _useFileStorage = !TryInitWindowsAppSdk();
        if (_useFileStorage)
        {
            Console.Error.WriteLine("Note: Using file-based settings (Windows App SDK not initialized).");
        }

        if (args.Length == 0)
            return RunServe();

        var first = args[0].ToLowerInvariant();
        if (first is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }
        if (first == "serve")
            return RunServe();
        if (first == "config")
            return RunConfig(args.AsSpan(1));
        if (first == "status")
            return RunStatus();

        Console.Error.WriteLine($"Error: Unknown command '{first}'.");
        Console.Error.WriteLine("Use --help for usage.");
        return 1;
    }

    private static bool TryInitWindowsAppSdk()
    {
        try
        {
            return Bootstrap.TryInitialize(0x00010008, out _);
        }
        catch
        {
            return false;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"local-translate-provider - Local translation provider (DeepL/Google format)

Usage:
  local-translate-provider              Start GUI
  local-translate-provider serve       Run HTTP server in background
  local-translate-provider config      Modify settings
  local-translate-provider status      Show service status
  local-translate-provider --help      Show this help

Commands:
  serve              Load settings, start HTTP server, wait for Ctrl+C
  config --port N    Set HTTP port (1-65535) and save
  status             Print translation backend status");
    }

    private static Task<AppSettings> LoadSettingsAsync() =>
        _useFileStorage ? FileSettingsStorage.LoadAsync() : SettingsService.LoadAsync();

    private static Task SaveSettingsAsync(AppSettings s) =>
        _useFileStorage ? FileSettingsStorage.SaveAsync(s) : SettingsService.SaveAsync(s);

    private static int RunServe()
    {
        try
        {
            var settings = LoadSettingsAsync().GetAwaiter().GetResult();
            var translationService = new TranslationService(settings);
            var server = new HttpTranslationServer(settings, translationService);
            server.Start();

            Console.WriteLine($"Server running on http://localhost:{settings.Port}/");
            Console.WriteLine("Press Ctrl+C to stop.");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                Task.Delay(Timeout.Infinite, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }

            server.Stop();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int RunConfig(ReadOnlySpan<string> args)
    {
        try
        {
            var settings = LoadSettingsAsync().GetAwaiter().GetResult();
            var modified = false;

            for (var i = 0; i < args.Length; i++)
            {
                if ((args[i] is "--port" or "-p") && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[i + 1], out var port) || port < 1 || port > 65535)
                    {
                        Console.Error.WriteLine("Error: Invalid port. Use 1-65535.");
                        return 1;
                    }
                    settings.Port = port;
                    modified = true;
                    i++;
                }
            }

            if (!modified)
            {
                Console.Error.WriteLine("Error: No changes specified. Use config --port N");
                return 1;
            }

            SaveSettingsAsync(settings).GetAwaiter().GetResult();
            Console.WriteLine($"Saved. Port set to {settings.Port}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int RunStatus()
    {
        try
        {
            var settings = LoadSettingsAsync().GetAwaiter().GetResult();
            var translationService = new TranslationService(settings);
            var status = translationService.GetStatusAsync().GetAwaiter().GetResult();

            Console.WriteLine($"Backend: {settings.TranslationBackend}");
            Console.WriteLine($"Ready: {status.IsReady}");
            Console.WriteLine($"Message: {status.Message}");
            if (!string.IsNullOrEmpty(status.Detail))
                Console.WriteLine($"Detail: {status.Detail}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
