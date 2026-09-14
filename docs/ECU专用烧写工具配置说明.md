# ECU 专用烧写工具配置说明

`DiagnosticFlashTool.Product.EcuX` 是从完整工程中分离出的产品壳。完整的 `DiagnosticFlashTool.App` 继续保留并参与解决方案构建；专用工具通过 Application 会话层复用 Core/Infrastructure 中的 CAN、ISO-TP、UDS、固件解析和刷写能力。

## 发布配置

产品默认值位于：

- `resources/product/ecu-profile.json`：设备、CAN 地址、帧格式、UDS 时间、Seed&Key DLL ABI 和固件槽位。
- `resources/product/BOOT/ECU-X.json`：固定 ECU 的 UDS 刷写步骤。

当前 CAN 地址、帧格式、波特率和 BIN 回退地址是示例值。接入真实 ECU 前必须按通信矩阵和 Bootloader 规范确认并替换。产品 JSON 随发布包管理；用户选择的固件、DLL 和时间参数写入 `%LocalAppData%\DiagnosticFlashTool\EcuX\settings.json`。

发布命令：

```powershell
dotnet publish src/DiagnosticFlashTool.Product.EcuX/DiagnosticFlashTool.Product.EcuX.csproj -c Release
```

默认发布目录为 `src/DiagnosticFlashTool.Product.EcuX/bin/Release/net8.0-windows/publish/`。发布后应保留主程序、`DiagnosticFlashTool.SeedKeyWorker.exe`、`resources/product/` 和 `resources/native/` 的相对位置。

## Seed&Key DLL

算法通过独立的 x86 `DiagnosticFlashTool.SeedKeyWorker.exe` 调用，支持：

- `GenerateKeyEx(seed, seedSize, level, variant, key, maxKeySize, actualKeySize)`
- `GenerateKeyExOpt(seed, seedSize, level, variant, options, key, maxKeySize, actualKeySize)`
- `cdecl` 和 `stdcall`

当前 Worker 固定为 x86，以兼容历史算法 DLL。选择 x64 DLL 时会在调用前给出位数不匹配错误；如目标算法只提供 x64 版本，应另建 x64 Worker 发布变体。

当前发布为 framework-dependent：目标机需要安装 x64 .NET 8 Desktop Runtime，调用 x86 算法 DLL 时还需要安装 x86 .NET 8 Runtime。若交付环境不允许安装运行时，应在正式打包阶段把主程序和 Worker 分别制作为对应架构的 self-contained 发布物。

## 实车接入检查

1. 确认总线类型。当前实现为经典 CAN/ISO-TP，不包含参考界面中的 LIN 适配器。
2. 确认物理请求 ID、功能请求 ID、响应 ID以及标准帧/扩展帧。
3. 确认 P2、P2*、S3 和 ResponsePending 总超时。
4. 确认 0x27 子功能与传给 DLL 的 security level、variant、options。
5. 确认擦除、校验、CRC 例程及固件地址。当前示例流程只包含会话、安全访问、下载和复位。
6. 使用 Mock 跑通配置和文件解析后，再以台架电源和目标 ECU 做异常断电、NRC、超时及重复刷写验证。
