using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using TransLocal;

namespace TransLocal.Services;

/// <summary>
/// Named Pipe 客户端，用于 CLI 向已运行的托盘实例发送命令。
/// </summary>
public static class IpcClient
{
    private const string PipeName = "TransLocal_IPC";

    public static async Task<(bool Success, string? Response)> SendAsync(string command)
    {
        var cmd = command.Trim().ToLowerInvariant();
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            await pipe.ConnectAsync(3000).ConfigureAwait(false);
            DebugLog.Write($"[IpcClient] Connect done, cmd={cmd}");

            var cmdBytes = Encoding.UTF8.GetBytes(cmd + "\n");
            await pipe.WriteAsync(cmdBytes.AsMemory(0, cmdBytes.Length)).ConfigureAwait(false);
            await pipe.FlushAsync().ConfigureAwait(false);
            DebugLog.Write($"[IpcClient] Write done, cmd={cmd}");

            if (cmd == "status")
            {
                using var ms = new MemoryStream();
                var buffer = new byte[4096];
                int n;
                while ((n = await pipe.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
                    ms.Write(buffer, 0, n);
                var response = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                DebugLog.Write($"[IpcClient] Read done, len={response?.Length ?? 0}");
                return (true, response);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[IpcClient] Exception, cmd={cmd}: {ex.GetType().Name} {ex.Message}");
            return (false, null);
        }
    }

    public static (bool Success, string? Response) Send(string command) =>
        SendAsync(command).GetAwaiter().GetResult();
}
