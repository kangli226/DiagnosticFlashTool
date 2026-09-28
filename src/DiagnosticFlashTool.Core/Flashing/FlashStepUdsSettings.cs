using DiagnosticFlashTool.Core.Diagnostics;

namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>解析流程步骤中与 UDS 请求相关的公共设置。</summary>
internal static class FlashStepUdsSettings
{
    public static UdsAddressing ParseAddressing(string? value)
    {
        return string.Equals(value, "functional", StringComparison.OrdinalIgnoreCase)
            ? UdsAddressing.Functional
            : UdsAddressing.Physical;
    }

    public static void EnsurePositive(UdsResponse response)
    {
        try
        {
            response.EnsurePositive();
        }
        catch (InvalidOperationException ex)
        {
            throw new FlashExecutionException(
                FlashFailureKind.Protocol,
                $"UDS 响应校验失败：{ex.Message}",
                ex);
        }
    }
}
