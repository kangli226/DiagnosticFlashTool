# AGENTS.md

.NET 8 WPF/MVVM 汽车 ECU BOOT 刷写工具（DiagnosticFlashTool）。本文件帮助 AI 编程代理快速理解代码库并高效工作。

## 项目概览

三个项目，依赖方向单向：`App → (Core, Infrastructure)`，`Infrastructure → Core`，**Core 无任何外部依赖**。

| 项目 | 说明 |
|------|------|
| `DiagnosticFlashTool.Core` | CAN 抽象、ISO-TP、UDS、固件解析、刷写流程执行（纯 net8.0 类库，无 UI/Windows 依赖） |
| `DiagnosticFlashTool.Infrastructure` | JSON 仓储、CAN 设备实现（Mock / ZLG 原生） |
| `DiagnosticFlashTool.App` | WPF 桌面 UI + MVVM 状态（net8.0-windows，MaterialDesignThemes 5.3.0） |

功能范围见 [README.md](README.md)（不要复制其中的内容）。

## 构建与运行

```powershell
dotnet build DiagnosticFlashTool.slnx -c Debug
```

产物：`src/DiagnosticFlashTool.App/bin/Debug/net8.0-windows/DiagnosticFlashTool.exe`。
方案为 `.slnx` 格式（新版 XML 方案，非传统 `.sln`）。

## 架构约定

### 分层
- 业务逻辑放 **Core**；接口定义在 Core，实现放 Infrastructure（如 `ICanDevice` → `MockCanDevice`/`ZlgCanDevice`）。
- 解耦通过工厂（`CanDeviceFactory.Create(deviceType)`）与注册表（`SeedKeyAlgorithmRegistry`）完成。
- 给 Core 新增代码时不得引入 WPF/Windows 专属类型，保持可被任何层引用。

### MVVM（App）
- ViewModel 继承 `ObservableObject`，用 `SetProperty<T>(ref field, value)` 触发通知。
- 命令：同步用 `RelayCommand`，异步用 `AsyncRelayCommand`（签名 `Func<CancellationToken, Task>`，内部自动防重入）。
- **无 DI 容器**：依赖在 `MainViewModel` 构造函数中手动 new/装配，新增依赖请沿用该模式。
- 集合用 `ObservableCollection<T>` + `[]` 集合表达式初始化。
- 新页面需挂到 shell 导航（`MainViewModel.SelectedShellIndex`），视图代码隐藏按现有 `Views/*.xaml.cs` 模式写事件处理。

### 代码风格
- 文件范围命名空间 `namespace DiagnosticFlashTool.X;`；类默认 `sealed`。
- 简单不可变模型用 `record`（如 `CanFrame`、`FlashProgress`）。
- 项目已启用 `Nullable` + `ImplicitUsings`，不要关闭。
- `App/GlobalUsings.cs` 已对 WPF 类型做别名（如 `MessageBox`、`Color`、`Key`），避免引入冲突 using。
- UI 文案为中文（如 "刷写完成"），新增文案保持一致。

## 配置与 JSON

- 配置模型用 `[JsonPropertyName("...")]` 显式映射；注意 `ProjectConfigEntry` 的属性名大小写与 JSON 键并不一致，务必以 `JsonPropertyName` 为准。
- 序列化选项约定：`PropertyNameCaseInsensitive = true`、`WriteIndented = true`；BOOT 配置额外开启 `ReadCommentHandling = JsonCommentHandling.Skip`、`AllowTrailingCommas = true`。
- **保存 BOOT JSON 时必须保留 scripts/transport 及未编辑字段**，只重写 `flow` 数组（`JsonBootConfigRepository.Save`）。
- 配置路径统一经 `AppConfigurationPaths`：默认 `resources/configs`（`projects.json`、`BOOT/`、`FormulaDatabase/`、`Scripts/`）；应用设置 `settings.json` 在 `%LocalAppData%\DiagnosticFlashTool\`。

## CAN / UDS / 刷写（Core）

- 设备类型：`Mock`（离线验证）与 `ZLG USBCAN-2A (4)`（映射 ZLG 类型 4）。
- ZLG 原生支持：`resources/native` 下的 `ControlCAN.dll`/`kerneldlls` 由 csproj 拷贝到输出目录；**`ZlgCanDevice` 打开设备前必须设置原生 DLL 搜索路径**，改动该适配器时保持此行为。
- UDS：`UdsClient` 基于 `IsoTpTransport`；0x78 pending 响应需轮询等待（`UdsClient.SendAsync` 已处理）。
- 安全访问 0x27：seed/key 经 `SeedKeyAlgorithmRegistry` 按名解析（默认 `AES128_OneFunc`，键不区分大小写）。新增算法：实现 `ISeedKeyAlgorithm` 并在注册表中 `Register`。
- 固件格式：S19/SREC/MOT、Intel HEX、BIN（`FirmwareLoader`/`HexParser`/`S19Parser`）。
- 刷写流程由 `FlashFlowExecutor` 按 `flow` 步骤执行；步骤类型 `DownloadDriver`/`DownloadApplication` 或普通 UDS 步骤。

## 线程与 UI 注意

- 长时间操作走 `AsyncRelayCommand` + `CancellationToken`，不得阻塞 UI 线程。
- 托盘（NotifyIcon）回调需用 `Dispatcher.Invoke` 切回 UI 线程（见 `MainWindow`）。
- XAML 遵循 `Styles/` 主题（MaterialDesignThemes 5.3.0），避免硬编码颜色；资源在 `App.xaml` 合并 `Styles/AppStyles.xaml`。
- `resources/**` 需保持 `CopyToOutputDirectory=PreserveNewest` 拷贝规则。

## 测试与验证

- 无自动化测试项目；离线功能/流程验证使用设备类型 `Mock`。
- 修改 CAN/UDS/刷写逻辑后，建议用 Mock 设备跑通典型流程验证。
