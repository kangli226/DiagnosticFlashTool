# Fluent 迁移与发布说明

最近更新：2026-10-15。方案为 WPF + .NET 10 + 官方 Fluent 亮色主题。

当前已完成代码迁移和本机发布验证。下述验证基于提交 `01d55ecb2c61` 及本次未提交的工作区变更；目标机和硬件验收仍需按第 5 节执行。

## 1. 改动批次与结果

| 批次 | 改动 | 当前结果 |
| --- | --- | --- |
| P0 基线 | 确认主题、语义样式、进程架构和验证边界 | 采用固定亮色；保留 WPF/MVVM 分层与现有页面骨架 |
| P1 运行平台 | 将项目升级到 `net10.0` / `net10.0-windows`，增加 `global.json` | SDK 基线为 `10.0.401`，允许同功能带的补丁升级；主程序和 ECU-X 为 x64，Worker 为 x86 |
| P2 主题接入 | App 和 ECU-X 启用 `ThemeMode="Light"`，桥接产品强调色 | 主操作、选中和焦点颜色沿用产品配色 |
| P3 共享控件 | 标准按钮、文本框、密码框和下拉框使用官方 Fluent 样式，进度条使用主题模板 | 业务页面继续引用原有语义样式；开关、Shell 导航和业务卡片保留共享实现 |
| P4 页面与图标 | 页面、页头、导航和对话框改用共享 `FluentIcon`，删除 MaterialDesign 包依赖 | 图标优先使用 Segoe Fluent Icons，回退 Segoe MDL2 Assets；危险及断开按钮通过局部状态资源保留交互色 |
| P5 发布与验证 | 增加 x64 文件夹发布配置，隔离 Worker 架构参数，取消必需文件缺失时的静默跳过 | 两个 Release 发布包及本机资源校验通过；目标机验收待完成 |

精确颜色、尺寸、间距和控件状态仍以 `Styles/` 和相关 XAML 为准，设计规则见 [界面设计规范](界面设计规范.md)。当前未提供暗色主题或随系统切换主题的交付方案。

## 2. 构建、测试与发布

在仓库根目录执行：

```powershell
dotnet build DiagnosticFlashTool.slnx -c Debug
dotnet test DiagnosticFlashTool.slnx -c Debug
dotnet publish src/DiagnosticFlashTool.App/DiagnosticFlashTool.App.csproj -c Release -p:PublishProfile=Folder-win-x64
dotnet publish src/DiagnosticFlashTool.Product.EcuX/DiagnosticFlashTool.Product.EcuX.csproj -c Release -p:PublishProfile=Folder-win-x64
```

两个项目的发布配置均位于各自的 `Properties/PublishProfiles/Folder-win-x64.pubxml`。配置采用 framework-dependent、文件夹部署，关闭单文件发布与裁剪。

| 发布物 | 目录 | 目标机运行时 |
| --- | --- | --- |
| 通用主程序 | `artifacts/publish/DiagnosticFlashTool/` | x64 .NET 10 Desktop Runtime |
| ECU-X | `artifacts/publish/DiagnosticFlashTool.Product.EcuX/` | x64 .NET 10 Desktop Runtime；调用算法 Worker 还需 x86 .NET 10 Runtime |

发布配置不会自动清理输出目录。交付前应使用新的输出目录，或确认现有目录没有旧版程序集和配置残留；可用 `-o <新的输出目录>` 覆盖发布路径。交付完整目录，不单独提取 EXE。

`artifacts/` 为忽略的本机产物目录。发布到该目录不等于已分发给客户，也不生成安装程序。

## 3. 架构与依赖约束

- 通用主程序、ECU-X、`ControlCAN.dll` 及当前 `kerneldlls` 中的原生 DLL 均为 x64。保持 `resources/native/` 的相对路径，ZLG 适配器在打开设备前设置 DLL 搜索目录。
- SeedKey Worker 固定为 `win-x86`，与历史 x86 算法 DLL 在独立进程中运行。ECU-X 的 x64 发布参数不会传入 Worker；Worker 缺少必需输出文件会使构建或发布失败。
- Worker 的 EXE、DLL、`.deps.json`、`.runtimeconfig.json` 以及 Core/Infrastructure 共享程序集随 ECU-X 一起交付。不要把不同版本的这些文件混合使用。
- 当前发布配置及 Worker 依赖已安装的运行时。免安装运行时的 self-contained 包尚未实现；只将主程序改为 self-contained 不能消除 x86 Worker 的运行时要求。
- 官方 Fluent 主题由 .NET 10 WPF 提供；发布包不再包含 MaterialDesign 主题或图标程序集。升级 .NET/WPF 后仍需复核共享样式和状态资源。

ECU-X 产品配置、算法 DLL ABI 和实车参数要求见 [ECU 专用烧写工具配置说明](ECU专用烧写工具配置说明.md)。

## 4. 本机验证记录

验证环境：Windows x64（系统版本 `10.0.26200`）、.NET SDK `10.0.401`、x64/x86 .NET 运行时 `10.0.12`。

| 检查 | 结果及覆盖范围 |
| --- | --- |
| Debug 解决方案重新构建 | 0 警告、0 错误 |
| Application 自动化测试 | 46 项通过，0 失败、0 跳过；包含会话、流程校验、UDS 时间和 BOOT 仓储相关用例 |
| 临时 WPF 资源及运行检查 | 45 项通过：主题加载、字号样式、图标字形、按钮状态资源、禁用内容、连接状态切换、页面加载和对话框加载 |
| 系统字体 | 本机两套字体均包含当前 32 种图标映射使用的字形；尚未在仅安装回退字体的目标机上验收 |
| 最小尺寸渲染 | 按主窗口最小 `1180 × 760 DIP` 对应的 `924 × 682 DIP` 内容区渲染 8 个 Shell 页面；刷写页补充等待、忙碌/禁用和错误样例 |
| Release 发布 | 通用主程序和 ECU-X 的 `win-x64` 文件夹发布通过；显式 `-r win-x64 --self-contained false` 的 ECU-X 发布通过 |
| 架构和运行清单 | 主程序 x64、Worker x86，运行时清单为 .NET 10；两个发布包均无 MaterialDesign DLL |
| 发布资源一致性 | 通用主程序 25 个资源文件、ECU-X 15 个原生目录文件和 2 个产品配置文件与源码逐一校验哈希一致；Worker 必需文件及共享程序集与其构建输出一致 |
| Worker 启动 | 发布目录中的 x86 Worker 可启动；空输入返回预期 JSON 错误及退出码 2，确认运行时和协议入口可用 |

WPF 渲染使用隔离视图和模拟状态，不创建真实设备会话；8 个页面的首次渲染使用空 DataContext。它能检查资源加载及样例显示，不能证明真实业务绑定、所有滚动内容、键盘顺序和所有弹层均已验收。临时验证程序与截图保存在忽略的 `build-validation/fluent-runtime/` 下，不属于产品依赖或正式测试项目。

Worker 空输入检查未加载供应商算法 DLL，不能替代真实 Seed&Key 调用验证。

## 5. 目标机验收剩余项

- 在客户实际使用的 Windows 版本和干净环境中安装对应架构的 .NET 10 运行时，验证两个完整发布目录可启动；ECU-X 验证 x86 Worker 与真实算法 DLL。
- 在最小窗口及客户常用 DPI（100%、125%、150%、200%）下复核常规数据、长路径、滚动列表、错误提示和各弹层；验证 Tab、Shift+Tab、Enter、Escape 及鼠标悬停/按下反馈。
- 验证窗口缩放、最大化、关闭和托盘流程，以及仅使用 Segoe MDL2 Assets 回退字体时的图标显示。
- 使用 Mock 完成典型操作流程，再用目标 ZLG 设备、驱动及 ECU 通信矩阵进行连接和刷写回归。当前本机发布检查未验证真实驱动、USB 通信和实机刷写。

完成目标机验收后，在发布记录中补充代码版本、配置版本、Windows/驱动/硬件版本和剩余限制，再确认客户交付结论。
