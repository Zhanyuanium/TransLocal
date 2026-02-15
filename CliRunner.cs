using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace local_translate_provider;

public static class CliRunner
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    public static void InitConsole()
    {
        if (AttachConsole(AttachParentProcess))
        {
            var stdout = GetStdHandle(StdOutputHandle);
            var stderr = GetStdHandle(StdErrorHandle);
            if (stdout != IntPtr.Zero && stdout != new IntPtr(-1))
            {
                var stdoutStream = new FileStream(new SafeFileHandle(stdout, true), FileAccess.Write);
                Console.SetOut(new StreamWriter(stdoutStream) { AutoFlush = true });
            }
            if (stderr != IntPtr.Zero && stderr != new IntPtr(-1))
            {
                var stderrStream = new FileStream(new SafeFileHandle(stderr, true), FileAccess.Write);
                Console.SetError(new StreamWriter(stderrStream) { AutoFlush = true });
            }
        }
        else
        {
            AllocConsole();
        }
    }

    public static int Run(string[] args)
    {
        PrintHelp();
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"local-translate-provider - Local translation provider

Usage:
  local-translate-provider              Start GUI
  local-translate-provider serve        Run HTTP server
  local-translate-provider config       Modify settings
  local-translate-provider status       Show service status
  local-translate-provider --help       Show this help");
    }
}
