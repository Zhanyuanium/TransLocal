# IpcServer Status 死锁修复测试

## 修复说明

将 IpcServer 的 `getStatus` 从同步 `Func<string>` 改为异步 `Func<Task<string>>`，在管道线程直接 `await GetStatusAsync()`，避免在 UI 线程上执行 sync-over-async 导致死锁。

## 手动验证步骤

1. 构建主项目：`dotnet build TransLocal.csproj -p:Platform=x64`
2. 启动托盘实例：`.\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\TransLocal.exe --tray`
3. 等待约 3 秒后，在另一终端运行：`.\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\TransLocal.exe status`
4. 若修复有效，应快速返回后端状态（Backend、Ready、Message 等），不再卡住

## 单元测试

```bash
dotnet run --project Tests/IpcServerStatusTest/IpcServerStatusTest.csproj
```

- 无参数：基础管道通信测试
- `blocking` 参数：模拟 BlockingSyncContext + GetAwaiter().GetResult() 场景
