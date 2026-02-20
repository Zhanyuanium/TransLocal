# 调试日志说明

IPC 调试日志默认关闭，用于排查 status/gui 等子命令卡住问题。

## 启用方式

**方式一：主窗口 → 关于 → 启用 IPC 调试日志**（仅托盘进程，设置持久化）

**方式二：环境变量**（GUI 和 CLI 均生效）

```powershell
$env:TRANSLOCAL_DEBUG_LOG = "1"
```

**方式三：CLI 参数**（仅当前 CLI 进程生效）

```powershell
TransLocal status --debug-log
TransLocal gui --debug-log
```

完整日志需同时启用托盘端与 CLI：主窗口关于页开启，或先设置环境变量再启动托盘。

## 日志路径

`%TEMP%\TransLocal-debug.log`

## 定位卡点

找到最后一条日志，其下一步即为卡住位置。关键节点：

- `[IpcClient]`：Connect → Write → Read
- `[IpcServer]`：Connection accepted → ReadLine → _getStatusAsync → Write
