# AGENTS.md

.NET 10 WPF/MVVM 汽车 ECU BOOT 刷写工具（DiagnosticFlashTool）。本文件帮助 AI 编程代理快速理解代码库并高效工作。

## 项目概览

三个项目，依赖方向单向：`App → (Core, Infrastructure)`，`Infrastructure → Core`，**Core 无任何外部依赖**。

| 项目 | 说明 |
|------|------|
| `DiagnosticFlashTool.Core` | CAN 抽象、ISO-TP、UDS、固件解析、刷写流程执行（纯 net10.0 类库，无 UI/Windows 依赖） |
| `DiagnosticFlashTool.Infrastructure` | JSON 仓储、CAN 设备实现（Mock / ZLG 原生） |
| `DiagnosticFlashTool.App` | WPF 桌面 UI + MVVM 状态（net10.0-windows，官方 Fluent 主题及共享 FluentIcon） |

功能范围见 [README.md](README.md)（不要复制其中的内容）。

## 构建与运行

```powershell
dotnet build DiagnosticFlashTool.slnx -c Debug
```

产物：`src/DiagnosticFlashTool.App/bin/Debug/net10.0-windows/DiagnosticFlashTool.exe`。
方案为 `.slnx` 格式（新版 XML 方案，非传统 `.sln`）。·

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

### UI 设计与模板约束（强制）
- 新建或修改页面、控件、对话框、导航、样式及其他可见 UI 前，必须先完整阅读 [界面设计规范](docs/界面设计规范.md)，并检查本次改动涉及的现有页面与 `Styles/` 资源；不得跳过模板选择直接修改 XAML/C#。
- 开始编辑前，先在工作进度中简要说明页面类型、参考页面、复用资源、关键交互与状态，以及确有必要的模板偏离；局部修复可精简说明，但仍需核对相邻布局与既有样式。
- 新页面必须从规范中的标准页面骨架及最接近的现有页面开始设计，优先组合已有资源和复用控件；禁止复制样式定义、硬编码颜色或为单页重复创建已有控件模板。
- 精确的颜色、尺寸、间距和控件状态以当前 XAML 资源字典为唯一事实来源。文档、历史统计或示例与源码不一致时，以源码为准，并同步修正文档或明确记录有意偏离。
- 若同一新视觉或布局模式会在两个及以上位置使用，应在对应 `Styles/` 分类或 `Views/Controls/` 中抽取复用资源；不得把页面专属业务布局无条件提升为全局样式。
- 长表格和长列表默认隐藏非必要的垂直滚动条，保留鼠标滚轮、触控板和键盘滚动；WPF 使用 `ScrollViewer.VerticalScrollBarVisibility="Hidden"`，不得误用会禁用滚动的 `Disabled`。确需拖动滑块定位或需要显式滚动提示时可例外，并按 [界面设计规范](docs/界面设计规范.md) 说明和验证。
- UI 完成后必须按规范清单检查常规、空数据、忙碌/禁用、错误等适用状态，在最小窗口尺寸下确认无裁切、遮挡或文本溢出，并至少执行一次 Debug 构建；无法进行运行时检查时需明确说明。

### 代码风格
- 文件范围命名空间 `namespace DiagnosticFlashTool.X;`；类默认 `sealed`。
- 简单不可变模型用 `record`（如 `CanFrame`、`FlashProgress`）。
- 项目已启用 `Nullable` + `ImplicitUsings`，不要关闭。
- `App/GlobalUsings.cs` 已对 WPF 类型做别名（如 `MessageBox`、`Color`、`Key`），避免引入冲突 using。
- UI 文案为中文（如 "刷写完成"），新增文案保持一致。

### Git 提交信息规范
- 所有自动生成或建议的 Git 提交信息必须使用中文；仅保留 Conventional Commits 所需的英文 `type` 关键字。
- 首行格式为 `<type>(<scope>): <中文主题>`，无明确范围时省略 `(scope)`；冒号后必须有一个空格。
- `type` 必须使用小写的 `feat`、`fix`、`refactor`、`style`、`perf`、`test`、`docs`、`build`、`ci`、`chore` 或 `revert`。
- 中文主题应使用祈使式、准确描述变更，控制在 72 个字符以内，不以句号或其他结束标点收尾；禁止生成仅有英文的泛化文案（例如 `Refactor code structure...`）。
- 需要补充说明时，首行后空一行，正文继续使用中文，说明变更内容与原因；多项内容使用 `-` 分行。
- 破坏性变更在 `type` 后加 `!`，并在正文中使用 `BREAKING CHANGE:` 明确说明影响和迁移方式。
- 示例：`feat(刷写): 支持 BIN 固件校验`、`fix(配置): 保存流程时保留传输参数`、`refactor(ui): 重构日志列表布局`。

## 配置与 JSON

- 配置模型用 `[JsonPropertyName("...")]` 显式映射；注意 `ProjectConfigEntry` 的属性名大小写与 JSON 键并不一致，务必以 `JsonPropertyName` 为准。
- 序列化选项约定：`PropertyNameCaseInsensitive = true`、`WriteIndented = true`；BOOT 配置额外开启 `ReadCommentHandling = JsonCommentHandling.Skip`、`AllowTrailingCommas = true`。
- **保存 BOOT JSON 时必须保留 scripts 及未编辑字段**，只重写 `flow` 数组（`JsonBootConfigRepository.Save`）。
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
- XAML 使用 .NET 10 WPF 官方 Fluent 主题，遵循 `Styles/` 语义资源，避免硬编码颜色；资源在 `App.xaml` 合并 `Styles/AppStyles.xaml`。图标统一使用 `FluentIcon`（Segoe Fluent Icons，回退 Segoe MDL2 Assets），不得重新引入 MaterialDesign 控件主题或图标依赖。
- `resources/**` 需保持 `CopyToOutputDirectory=PreserveNewest` 拷贝规则。

## 测试与验证

- 自动化测试位于 `tests/DiagnosticFlashTool.Application.Tests`，执行 `dotnet test DiagnosticFlashTool.slnx -c Debug`；离线功能/流程验证使用设备类型 `Mock`。
- 修改 CAN/UDS/刷写逻辑后，建议用 Mock 设备跑通典型流程验证。
