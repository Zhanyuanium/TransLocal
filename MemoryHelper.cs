using System.Runtime.InteropServices;

namespace TransLocal;

/// <summary>
/// 强制回收托管内存并归还工作集给系统，用于主窗口关闭后降低进程内存占用。
/// </summary>
internal static class MemoryHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool K32EmptyWorkingSet(IntPtr hProcess);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>
    /// 同步执行 GC + 归还工作集，用于打开主窗口前回收残留内存。会阻塞调用线程约 300–500ms。
    /// </summary>
    public static void TrimWorkingSetSync()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced);
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            if (!K32EmptyWorkingSet(process.Handle))
                _ = EmptyWorkingSet(process.Handle);
        }
        catch
        {
            // 忽略失败
        }
    }

    /// <summary>
    /// 在后台线程执行：GC 回收 + 归还工作集。调用后进程内存占用应显著下降。
    /// </summary>
    public static void TrimWorkingSetAsync()
    {
        _ = System.Threading.Tasks.Task.Run(TrimWorkingSetCore);
    }

    private static void TrimWorkingSetCore()
    {
        try
        {
            // 等待窗口完全销毁后再回收
            System.Threading.Thread.Sleep(300);
            GC.Collect(2, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced);

            using var process = System.Diagnostics.Process.GetCurrentProcess();
            if (!K32EmptyWorkingSet(process.Handle))
                _ = EmptyWorkingSet(process.Handle);
        }
        catch
        {
            // 忽略失败，不影响主流程
        }
    }
}
