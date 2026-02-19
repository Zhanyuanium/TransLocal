using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using local_translate_provider;

namespace local_translate_provider.Services;

/// <summary>
/// Named Pipe 服务端，供 CLI 向已运行的托盘实例发送 gui/quit/reload/status/unload 命令。
/// </summary>
public sealed class IpcServer : IDisposable
{
    private const string PipeName = "LocalTranslateProvider_IPC";
    private readonly Action _onGui;
    private readonly Action _onQuit;
    private readonly Action _onReload;
    private readonly Action _onUnload;
    private readonly Func<Task<string>> _getStatusAsync;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public IpcServer(Action onGui, Action onQuit, Action onReload, Action onUnload, Func<Task<string>> getStatusAsync)
    {
        _onGui = onGui;
        _onQuit = onQuit;
        _onReload = onReload;
        _onUnload = onUnload;
        _getStatusAsync = getStatusAsync;
    }

    public void Start()
    {
        if (_listenTask != null) return;
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listenTask = null;
    }

    public void Dispose() => Stop();

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                DebugLog.Write("[IpcServer] Connection accepted");

                var cmd = await ReadLineAsync(pipe).ConfigureAwait(false);
                DebugLog.Write($"[IpcServer] ReadLine done, cmd={cmd}");

                switch (cmd)
                {
                    case "gui":
                        _onGui();
                        break;
                    case "quit":
                        _onQuit();
                        break;
                    case "reload":
                        _onReload();
                        break;
                    case "unload":
                        _onUnload();
                        break;
                    case "status":
                        DebugLog.Write("[IpcServer] _getStatusAsync start");
                        var status = await _getStatusAsync().ConfigureAwait(false);
                        DebugLog.Write($"[IpcServer] _getStatusAsync done, len={status?.Length ?? 0}");
                        var statusBytes = Encoding.UTF8.GetBytes(status + "\n");
                        await pipe.WriteAsync(statusBytes.AsMemory(0, statusBytes.Length)).ConfigureAwait(false);
                        await pipe.FlushAsync().ConfigureAwait(false);
                        DebugLog.Write("[IpcServer] Write done");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[IpcServer] Exception: {ex.GetType().Name} {ex.Message}");
            }
        }
    }

    private static async Task<string> ReadLineAsync(Stream pipe)
    {
        var bytes = new List<byte>();
        var buffer = new byte[256];
        while (true)
        {
            var n = await pipe.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            if (n <= 0) break;
            for (var i = 0; i < n; i++)
            {
                if (buffer[i] == (byte)'\n')
                    return Encoding.UTF8.GetString(bytes.ToArray()).Trim().ToLowerInvariant();
                bytes.Add(buffer[i]);
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray()).Trim().ToLowerInvariant();
    }
}
