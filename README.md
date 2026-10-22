# DiagnosticFlashTool

.NET 10 WPF/MVVM rewrite of the diagnostic boot flashing tool.

The desktop UI uses the official WPF Fluent light theme with a stable product accent. Shared icons use Segoe Fluent Icons with Segoe MDL2 Assets fallback; no third-party UI theme package is required.

## Projects

- `DiagnosticFlashTool.App`: WPF desktop UI and MVVM state.
- `DiagnosticFlashTool.Core`: CAN model, ISO-TP, UDS, firmware parsing, flash flow execution.
- `DiagnosticFlashTool.Infrastructure`: JSON repositories and CAN device implementations.

## Version 2 Scope

- Flash page: CAN connection, project selection, BOOT config selection, firmware selection, flash start, progress and log.
- Projects page: edit, add, delete and save `projects.json`.
- Flow page: load, edit, validate and save BOOT flow JSON steps.
- CAN page: manual classic CAN frame send and live TX/RX frame monitor.
- Native ZLG support: `ControlCAN.dll` and `kerneldlls` are copied to output under `resources/native`, and the ZLG adapter sets the native DLL search path before opening the device.
- Mock support: use `Mock` device type for offline flow validation without hardware.

## Build

```powershell
dotnet build DiagnosticFlashTool.slnx -c Debug
```

The executable is generated at:

```text
src/DiagnosticFlashTool.App/bin/Debug/net10.0-windows/DiagnosticFlashTool.exe
```

## Publish

```powershell
dotnet publish src/DiagnosticFlashTool.App/DiagnosticFlashTool.App.csproj -c Release -p:PublishProfile=Folder-win-x64
dotnet publish src/DiagnosticFlashTool.Product.EcuX/DiagnosticFlashTool.Product.EcuX.csproj -c Release -p:PublishProfile=Folder-win-x64
```

The framework-dependent x64 packages are generated under `artifacts/publish/DiagnosticFlashTool/` and `artifacts/publish/DiagnosticFlashTool.Product.EcuX/`. Both need the x64 .NET 10 Desktop Runtime; ECU-X also needs the x86 .NET 10 Runtime for its SeedKey worker. See the migration and publishing notes below for validation coverage.

## Documentation

- [软件功能说明](docs/软件功能说明.md): current functional baseline for development, testing and acceptance.
- [Fluent 迁移与发布说明](docs/Fluent迁移与发布说明.md): migration batches, publishing profiles, local verification and remaining acceptance checks.
- [ECU 专用烧写工具配置说明](docs/ECU专用烧写工具配置说明.md): ECU-X publishing, Seed&Key DLL and vehicle integration notes.

## Notes

- Device type `Mock` is intended for offline UI and protocol-flow checks.
- Device type `ZLG USBCAN-2A (4)` maps to ZLG device type `4`.
- BOOT JSON saving preserves scripts and non-edited fields, and rewrites the `flow` array from the Flow page.
- Firmware formats supported in this version: S19/SREC/MOT, Intel HEX and BIN.
