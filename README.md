# DiagnosticFlashTool

.NET 8 WPF/MVVM rewrite of the diagnostic boot flashing tool.

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
src/DiagnosticFlashTool.App/bin/Debug/net8.0-windows/DiagnosticFlashTool.exe
```

## Notes

- Device type `Mock` is intended for offline UI and protocol-flow checks.
- Device type `ZLG USBCAN-2A (4)` maps to ZLG device type `4`.
- BOOT JSON saving preserves scripts, transport and non-edited fields, and rewrites the `flow` array from the Flow page.
- Firmware formats supported in this version: S19/SREC/MOT, Intel HEX and BIN.
