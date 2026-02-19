using System.Diagnostics;
using System.IO;
using System.Linq;
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
        if (Array.Exists(args, a => a.Equals("--debug-log", StringComparison.OrdinalIgnoreCase)))
            DebugLog.IsEnabled = true;
        args = args.Where(a => !a.Equals("--debug-log", StringComparison.OrdinalIgnoreCase)).ToArray();

        _useFileStorage = !TryInitWindowsAppSdk();
        if (_useFileStorage)
        {
            Console.Error.WriteLine("Note: Using file-based settings (Windows App SDK not initialized).");
        }

        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var first = args[0].ToLowerInvariant();
        if (first is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }
        if (first == "gui")
            return RunGui();
        if (first == "quit")
            return RunQuit();
        if (first == "status")
            return RunStatus();
        if (first == "about")
            return RunAbout();
        if (first == "config")
            return RunConfig(args.AsSpan(1));

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
  local-translate-provider              Start tray (background), exit immediately
  local-translate-provider gui         Open main window
  local-translate-provider quit        Exit the running app
  local-translate-provider status      Show translation backend status
  local-translate-provider about       Show app info
  local-translate-provider config      Modify settings (see config --help)
  local-translate-provider --help      Show this help

Commands:
  gui                 Open main window (or start app and show)
  quit                Exit the running app
  status              Print backend status (from running app or standalone)
  about               Print app name and version
  config general      General settings (--run-at-startup, --minimize-tray)
  config model        Model settings (--backend, --model, --strategy, --device, --unload)
  config service      Service settings (--port, --deeple, --google, --api-key)

Options:
  --debug-log         Enable IPC debug log (use with status, gui, etc.)");
    }

    private static Task<AppSettings> LoadSettingsAsync() =>
        _useFileStorage ? FileSettingsStorage.LoadAsync() : SettingsService.LoadAsync();

    private static Task SaveSettingsAsync(AppSettings s) =>
        _useFileStorage ? FileSettingsStorage.SaveAsync(s) : SettingsService.SaveAsync(s);

    private static int RunGui()
    {
        var (ok, _) = IpcClient.Send("gui");
        if (ok)
        {
            return 0;
        }
        SpawnTrayWithWindow();
        return 0;
    }

    private static int RunQuit()
    {
        var (ok, _) = IpcClient.Send("quit");
        return 0;
    }

    private static int RunStatus()
    {
        var (ok, response) = IpcClient.Send("status");
        if (ok && !string.IsNullOrEmpty(response))
        {
            Console.WriteLine(response);
            return 0;
        }
        return RunStatusStandalone();
    }

    private static int RunStatusStandalone()
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

    private static int RunAbout()
    {
        Console.WriteLine("Local Translate Provider");
        Console.WriteLine("Local translation provider with DeepL/Google format endpoints.");
        return 0;
    }

    private static void SpawnTrayWithWindow()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "local-translate-provider.exe";
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "--tray --show-window",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }

    private static int RunConfig(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Use config general | config model | config service");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args.Length > 1 ? args.Slice(1) : ReadOnlySpan<string>.Empty;

        if (sub == "general")
            return RunConfigGeneral(rest);
        if (sub == "model")
            return RunConfigModel(rest);
        if (sub == "service")
            return RunConfigService(rest);

        Console.Error.WriteLine($"Error: Unknown config section '{sub}'.");
        return 1;
    }

    private static int RunConfigGeneral(ReadOnlySpan<string> args)
    {
        try
        {
            var settings = LoadSettingsAsync().GetAwaiter().GetResult();
            var modified = false;

            for (var i = 0; i < args.Length; i++)
            {
                if ((args[i] is "--run-at-startup") && i + 1 < args.Length)
                {
                    settings.RunAtStartup = ParseBool(args[i + 1]);
                    modified = true;
                    i++;
                }
                else if ((args[i] is "--minimize-tray") && i + 1 < args.Length)
                {
                    settings.MinimizeToTrayOnStartup = ParseBool(args[i + 1]);
                    modified = true;
                    i++;
                }
            }

            if (!modified)
            {
                Console.Error.WriteLine("Error: No changes. Use --run-at-startup true|false, --minimize-tray true|false");
                return 1;
            }

            SaveSettingsAsync(settings).GetAwaiter().GetResult();
            IpcClient.Send("reload");
            Console.WriteLine("Saved.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int RunConfigModel(ReadOnlySpan<string> args)
    {
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is "--unload")
                {
                    var (ok, _) = IpcClient.Send("unload");
                    Console.WriteLine(ok ? "Model unloaded." : "No running app to unload from.");
                    return ok ? 0 : 1;
                }
            }

            var settings = LoadSettingsAsync().GetAwaiter().GetResult();
            var modified = false;

            for (var i = 0; i < args.Length; i++)
            {
                if ((args[i] is "--backend") && i + 1 < args.Length)
                {
                    settings.TranslationBackend = args[i + 1].Equals("PhiSilica", StringComparison.OrdinalIgnoreCase)
                        ? TranslationBackend.PhiSilica : TranslationBackend.FoundryLocal;
                    modified = true;
                    i++;
                }
                else if ((args[i] is "--model") && i + 1 < args.Length)
                {
                    settings.FoundryModelAlias = args[i + 1].Trim();
                    modified = true;
                    i++;
                }
                else if ((args[i] is "--strategy") && i + 1 < args.Length)
                {
                    settings.ExecutionStrategy = args[i + 1].ToLowerInvariant() switch
                    {
                        "powersaving" => FoundryExecutionStrategy.PowerSaving,
                        "highperformance" => FoundryExecutionStrategy.HighPerformance,
                        "manual" => FoundryExecutionStrategy.Manual,
                        _ => settings.ExecutionStrategy
                    };
                    modified = true;
                    i++;
                }
                else if ((args[i] is "--device") && i + 1 < args.Length)
                {
                    settings.ManualDeviceType = args[i + 1].ToUpperInvariant() switch
                    {
                        "CPU" => FoundryDeviceType.CPU,
                        "GPU" => FoundryDeviceType.GPU,
                        "NPU" => FoundryDeviceType.NPU,
                        _ => settings.ManualDeviceType
                    };
                    modified = true;
                    i++;
                }
            }

            if (!modified)
            {
                Console.Error.WriteLine("Error: No changes. Use --backend, --model, --strategy, --device, --unload");
                return 1;
            }

            SaveSettingsAsync(settings).GetAwaiter().GetResult();
            IpcClient.Send("reload");
            Console.WriteLine("Saved.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int RunConfigService(ReadOnlySpan<string> args)
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
                else if ((args[i] is "--deeple") && i + 1 < args.Length)
                {
                    settings.EnableDeepLEndpoint = ParseBool(args[i + 1]);
                    modified = true;
                    i++;
                }
                else if ((args[i] is "--google") && i + 1 < args.Length)
                {
                    settings.EnableGoogleEndpoint = ParseBool(args[i + 1]);
                    modified = true;
                    i++;
                }
                else if ((args[i] is "--api-key") && i + 1 < args.Length)
                {
                    var v = args[i + 1].Trim();
                    settings.ApiKey = string.IsNullOrEmpty(v) ? null : v;
                    modified = true;
                    i++;
                }
            }

            if (!modified)
            {
                Console.Error.WriteLine("Error: No changes. Use --port N, --deeple, --google, --api-key");
                return 1;
            }

            SaveSettingsAsync(settings).GetAwaiter().GetResult();
            IpcClient.Send("reload");
            Console.WriteLine($"Saved. Port: {settings.Port}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static bool ParseBool(ReadOnlySpan<char> v)
    {
        var s = v.Trim().ToString();
        return s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               s.Equals("1", StringComparison.Ordinal) ||
               s.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
