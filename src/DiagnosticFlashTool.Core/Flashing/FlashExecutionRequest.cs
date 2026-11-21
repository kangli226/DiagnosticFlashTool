using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;

namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>一次刷写流程执行所需的完整输入。</summary>
public sealed record FlashExecutionRequest(
    BootConfig BootConfig,
    FirmwareSet FirmwareSet,
    UdsTimingOptions? Timing = null)
{
    /// <summary>可选的实时日志接收器；执行结果仍保留完整日志，取消时也会报告已产生的日志。</summary>
    public IProgress<string>? LogProgress { get; init; }
}
