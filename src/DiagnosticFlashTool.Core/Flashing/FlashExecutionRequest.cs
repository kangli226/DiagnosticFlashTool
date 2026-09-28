using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;

namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>一次刷写流程执行所需的完整输入。</summary>
public sealed record FlashExecutionRequest(
    BootConfig BootConfig,
    FirmwareSet FirmwareSet,
    UdsTimingOptions? Timing = null);
