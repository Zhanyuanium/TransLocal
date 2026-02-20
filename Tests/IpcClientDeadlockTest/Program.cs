using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IpcClientDeadlockTest;

/// <summary>
/// 测试 sync-over-async + SynchronizationContext 死锁场景。
/// 使用与 IpcClient 相同的逻辑，验证 ConfigureAwait(false) 可避免死锁。
/// </summary>
internal static class Program
{
    private const string PipeName = "TransLocal_IPC_Test_Nonexistent";

    private static async Task<(bool Success, string? Response)> SendAsyncWithConfigureAwait(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            await pipe.ConnectAsync(2000).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(command).ConfigureAwait(false);
            return (true, null);
        }
        catch
        {
            return (false, null);
        }
    }

    private static async Task<(bool Success, string? Response)> SendAsyncWithoutConfigureAwait(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            await pipe.ConnectAsync(2000);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(command);
            return (true, null);
        }
        catch
        {
            return (false, null);
        }
    }

    private static int Main(string[] args)
    {
        var useConfigureAwait = args.Length > 0 && args[0] == "fixed";
        Console.WriteLine($"Test: {(useConfigureAwait ? "WITH ConfigureAwait(false) (fix)" : "WITHOUT ConfigureAwait (would deadlock)")}");
        Console.WriteLine("Simulating SyncContext + GetAwaiter().GetResult()...");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var completed = false;
        var result = (false, (string?)null);

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new BlockingSyncContext());
            var sw = Stopwatch.StartNew();
            try
            {
                result = useConfigureAwait
                    ? SendAsyncWithConfigureAwait("gui").GetAwaiter().GetResult()
                    : SendAsyncWithoutConfigureAwait("gui").GetAwaiter().GetResult();
            }
            finally
            {
                sw.Stop();
                completed = true;
                Console.WriteLine($"Elapsed: {sw.ElapsedMilliseconds}ms, Result: {result.Item1}");
            }
        });

        thread.Start();
        var joined = thread.Join(TimeSpan.FromSeconds(8));

        if (!completed || !joined)
        {
            Console.WriteLine("FAIL: Timed out (deadlock)");
            return 1;
        }

        Console.WriteLine("PASS: Returned within timeout");
        return 0;
    }

    private sealed class BlockingSyncContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }
}
