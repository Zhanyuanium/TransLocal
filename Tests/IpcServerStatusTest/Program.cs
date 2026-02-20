using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IpcServerStatusTest;

/// <summary>
/// 验证异步 getStatus 模式：服务端在管道线程直接 await GetStatusAsync，避免 UI 线程 sync-over-async 死锁。
/// 使用与 IpcServer 相同的逻辑，模拟 BlockingSyncContext + GetAwaiter().GetResult() 的 CLI 场景。
/// </summary>
internal static class Program
{
    private static readonly string PipeName = "TransLocal_IPC_" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<string> GetStatusAsync()
    {
        await Task.Delay(50).ConfigureAwait(false);
        return "Backend: Foundry\nReady: true\nMessage: OK";
    }

    private static int Main(string[] args)
    {
        var useBlockingContext = args.Length > 0 && args[0] == "blocking";
        Console.WriteLine("IpcServerStatusTest: Verify async getStatus avoids deadlock");
        Console.WriteLine("Server: await GetStatusAsync() on pipe thread (no UI sync-over-async)");
        Console.WriteLine($"Client: GetAwaiter().GetResult() {(useBlockingContext ? "WITH BlockingSyncContext" : "no SyncContext")}");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        var serverReady = new ManualResetEventSlim(false);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        serverReady.Set();
                        await pipe.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
                        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                        var cmd = (await reader.ReadLineAsync().ConfigureAwait(false))?.Trim()?.ToLowerInvariant();
                        if (cmd == "status")
                        {
                            var status = await GetStatusAsync().ConfigureAwait(false);
                            await writer.WriteLineAsync(status).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
        }, CancellationToken.None);

        if (!serverReady.Wait(TimeSpan.FromSeconds(2)))
        {
            Console.WriteLine("FAIL: Server not ready");
            cts.Cancel();
            return 1;
        }

        Thread.Sleep(300);

        var done = false;
        var (success, response) = (false, (string?)null);
        var sw = Stopwatch.StartNew();

        var clientThread = new Thread(() =>
        {
            if (useBlockingContext)
                SynchronizationContext.SetSynchronizationContext(new BlockingSyncContext());
            try
            {
                (success, response) = SendStatusAsync().GetAwaiter().GetResult();
            }
            finally
            {
                done = true;
            }
        });

        clientThread.Start();
        var joined = clientThread.Join(TimeSpan.FromSeconds(5));
        sw.Stop();
        cts.Cancel();
        try { serverTask.GetAwaiter().GetResult(); } catch { }

        if (!done || !joined)
        {
            Console.WriteLine($"FAIL: Timed out after {sw.ElapsedMilliseconds}ms (deadlock)");
            return 1;
        }

        if (!success || string.IsNullOrEmpty(response) || !response!.Contains("Ready: true"))
        {
            Console.WriteLine($"FAIL: Success={success}, Response={response}");
            return 1;
        }

        Console.WriteLine($"PASS: Response in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine(response);
        return 0;
    }

    private static async Task<(bool Success, string? Response)> SendStatusAsync()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            await pipe.ConnectAsync(4000).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("status").ConfigureAwait(false);
            var resp = await reader.ReadToEndAsync().ConfigureAwait(false);
            return (true, resp.Trim());
        }
        catch
        {
            return (false, null);
        }
    }

    private sealed class BlockingSyncContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { }
    }
}
