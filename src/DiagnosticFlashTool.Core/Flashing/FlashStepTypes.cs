namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>刷写流程步骤的可执行类别。</summary>
public enum FlashStepKind
{
    Uds,
    DownloadDriver,
    DownloadApplication
}

/// <summary>集中定义并解析 BOOT JSON 中的步骤类型字符串。</summary>
public static class FlashStepTypes
{
    public const string DownloadDriver = nameof(FlashStepKind.DownloadDriver);
    public const string DownloadApplication = nameof(FlashStepKind.DownloadApplication);

    public static bool TryParse(string? value, out FlashStepKind kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            kind = FlashStepKind.Uds;
            return true;
        }

        if (string.Equals(value, DownloadDriver, StringComparison.OrdinalIgnoreCase))
        {
            kind = FlashStepKind.DownloadDriver;
            return true;
        }

        if (string.Equals(value, DownloadApplication, StringComparison.OrdinalIgnoreCase))
        {
            kind = FlashStepKind.DownloadApplication;
            return true;
        }

        kind = FlashStepKind.Uds;
        return false;
    }

    public static bool IsDownload(string? value)
    {
        return TryParse(value, out var kind) && kind != FlashStepKind.Uds;
    }
}
