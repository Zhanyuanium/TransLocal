# TransLocal

本地翻译服务，提供 DeepL / Google Translate 兼容的 HTTP 接口，支持 Phi Silica 与 Foundry Local 两种后端，可在 Windows 上以托盘形式后台运行。

## 功能特性

- **HTTP 翻译接口**：兼容 DeepL 与 Google Translate 的 API 格式，便于现有翻译插件接入
- **API 代理**：可将系统代理设为本地服务，自动拦截 `api-free.deepl.com`、`api.deepl.com`、`translate.googleapis.com` 的 HTTPS 请求并本地翻译，无需修改应用配置
- **双后端**：
  - **Phi Silica**：基于 Windows AI，需 Copilot+ PC（NPU），中国区域不可用
  - **Foundry Local**：基于 Microsoft AI Foundry，支持 phi-3.5-mini、qwen2.5 等模型，可选用 CPU/GPU/NPU
- **托盘运行**：后台常驻，支持开机自启、启动时最小化到托盘
- **CLI 子命令**：`gui`、`quit`、`status`、`about`、`config`，通过 IPC 与托盘通信

## 系统要求

- Windows 10 17763 及以上
- .NET 8
- Foundry Local 需安装 [Foundry CLI](https://github.com/microsoft/AI-Foundry) 并下载模型

## 构建与运行

### 构建

```powershell
dotnet build -p:Platform=x64
```

### 运行方式

**方式一：MSIX 打包（推荐）**

在 Visual Studio 中选择 `TransLocal (Package)` 配置，生成并部署。安装后可通过开始菜单或 `TransLocal` 命令启动。

**方式二：直接运行**

```powershell
# 启动托盘（后台运行）
.\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\TransLocal.exe

# 或指定 --tray
.\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\TransLocal.exe --tray
```

## 命令行用法

```
TransLocal            启动托盘并退出
TransLocal gui        打开主窗口（或启动新实例）
TransLocal quit       退出已运行的托盘
TransLocal status     显示翻译后端状态
TransLocal about      显示应用信息
TransLocal config     修改设置（见 config --help）
TransLocal --help     显示帮助
```

### config 子命令

- `config general`：`--run-at-startup`、`--minimize-tray`
- `config model`：`--backend`、`--model`、`--strategy`、`--device`
- `config service`：`--port`、`--deeple`、`--google`、`--api-key`

## API 代理

在「服务」页点击「打开代理设置」，将系统代理手动设为 `127.0.0.1:52860` 后，浏览器或应用通过系统代理访问 DeepL/Google 翻译 API 时，请求会被拦截并转发到本地翻译服务。

**使用步骤：**

1. 点击「打开代理设置」，将系统代理设为 `127.0.0.1:52860`（端口与上方一致）
2. 导出 CA 证书并安装到系统「受信任的根证书颁发机构」（或点击「安装 CA 证书」尝试自动安装）

**拦截域名**：`api-free.deepl.com`、`api.deepl.com`、`translate.googleapis.com`

## HTTP 接口

默认端口：52860

### DeepL 格式

```
POST http://localhost:52860/v2/translate
Content-Type: application/json
Authorization: DeepL-Auth-Key <api-key>  # 可选

{"text": ["Hello"], "target_lang": "zh"}
```

### Google 格式

**新版（POST）**

```
POST http://localhost:52860/language/translate/v2
Content-Type: application/json
Authorization: Bearer <api-key>  # 可选

{"q": ["Hello"], "target": "zh", "source": "en"}
```

**旧版（GET，translate_a 格式）**

```
GET http://localhost:52860/translate_a/single?q=Hello&sl=en&tl=zh
```

## 配置说明

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| Port | HTTP 端口 | 52860 |
| EnableDeepLEndpoint | 启用 DeepL 格式接口 | true |
| EnableGoogleEndpoint | 启用 Google 格式接口 | true |
| ApiKey | 可选 API 密钥 | - |
| TranslationBackend | 翻译后端 | FoundryLocal |
| FoundryModelAlias | Foundry 模型别名 | phi-4-mini |
| ExecutionStrategy | 运行策略 | HighPerformance |
| RunAtStartup | 开机自启 | false |
| MinimizeToTrayOnStartup | 启动时最小化到托盘 | true |

## 调试日志

默认关闭。启用方式：

- 主窗口 → 关于 → 启用 IPC 调试日志
- 环境变量 `TRANSLOCAL_DEBUG_LOG=1`
- CLI 参数 `--debug-log`

详见 [DEBUG_LOG_README.md](DEBUG_LOG_README.md)。

## 项目结构

```
TransLocal/
├── ApiAdapters/          # DeepL / Google API 请求解析
├── Models/               # AppSettings 等
├── Pages/                # 主窗口设置页（通用 / 模型 / 服务 / 关于）
├── Services/             # 翻译服务、HTTP 服务、IPC、证书管理、系统代理
├── TrayIcon/             # 托盘图标
├── Tests/                # IPC 相关测试
└── Assets/               # 应用图标
```

## FAQ

**如何卸载 TransLocal CA 证书？**

若你曾通过设置页「安装 CA 证书」将 TransLocal CA 安装到系统，可手动卸载：按 `Win+R`，输入 `certmgr.msc` 回车，在「受信任的根证书颁发机构」→「证书」下找到名为 **TransLocal CA** 的条目，右键 →「删除」即可。该证书仅安装在当前用户存储中，删除后浏览器等应用将不再信任由 TransLocal 签发的代理证书。

**卸载模型后，任务管理器显示内存已降，但本机内存占用并未下降，RamMap 中本应用进程的 Modified 仍有数 GB？**

这是 Windows 的正常行为。卸载时进程的 Private 会下降，但曾被模型占用的物理页会进入 Modified 列表（脏页），需由系统在内存紧张时回收。EmptyWorkingSet 只影响工作集，无法直接释放 Modified 页。这些页会在系统需要内存时自动回收，无需额外操作。

## 许可证

MIT
