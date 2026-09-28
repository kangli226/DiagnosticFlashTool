namespace DiagnosticFlashTool.Core.Diagnostics;

/// <summary>UDS 客户端时序配置，所有数值的单位均为毫秒。</summary>
public sealed class UdsTimingOptions
{
    /// <summary>等待首个响应（包括 NRC 0x78）的兜底超时。</summary>
    public int P2ClientMs { get; init; } = 1000;

    /// <summary>收到 NRC 0x78 后，等待下一条响应的兜底超时。</summary>
    public int P2StarClientMs { get; init; } = 6000;

    /// <summary>目标 ECU 的 S3 服务端会话失活时间，仅用于约束 Tester Present 周期。</summary>
    public int S3ServerTimeoutMs { get; init; } = 5000;

    /// <summary>Tester Present 的实际发送周期；配置为 0 表示禁用。</summary>
    public int TesterPresentIntervalMs { get; init; } = 2000;

    /// <summary>连续收到 NRC 0x78 后等待最终响应的总上限；配置为 0 表示不设置总上限。</summary>
    public int PendingOverallTimeoutMs { get; init; } = 120_000;

    public TimeSpan P2ClientTimeout => TimeSpan.FromMilliseconds(ValidatePositive(P2ClientMs, nameof(P2ClientMs)));
    public TimeSpan P2StarClientTimeout => TimeSpan.FromMilliseconds(ValidatePositive(P2StarClientMs, nameof(P2StarClientMs)));
    public TimeSpan? PendingOverallTimeout => PendingOverallTimeoutMs <= 0
        ? null
        : TimeSpan.FromMilliseconds(PendingOverallTimeoutMs);

    public UdsTimingOptions Validate()
    {
        _ = P2ClientTimeout;
        _ = P2StarClientTimeout;
        if (S3ServerTimeoutMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(S3ServerTimeoutMs), "S3 服务端超时不能为负数。");
        }

        if (TesterPresentIntervalMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TesterPresentIntervalMs), "Tester Present 周期不能为负数。");
        }

        if (S3ServerTimeoutMs > 0
            && TesterPresentIntervalMs > 0
            && TesterPresentIntervalMs >= S3ServerTimeoutMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TesterPresentIntervalMs),
                "Tester Present 周期必须小于 S3 服务端超时。");
        }

        if (PendingOverallTimeoutMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PendingOverallTimeoutMs), "Pending 总超时不能为负数。");
        }

        return this;
    }

    public static UdsTimingOptions FromRequestTimeouts(TimeSpan p2, TimeSpan p2Star)
    {
        var p2Ms = checked((int)Math.Ceiling(p2.TotalMilliseconds));
        var p2StarMs = checked((int)Math.Ceiling(p2Star.TotalMilliseconds));
        return new UdsTimingOptions
        {
            P2ClientMs = Math.Max(1, p2Ms),
            P2StarClientMs = Math.Max(1, p2StarMs),
            S3ServerTimeoutMs = 0,
            TesterPresentIntervalMs = 0,
            PendingOverallTimeoutMs = Math.Max(1, p2StarMs)
        };
    }

    private static int ValidatePositive(int value, string parameterName)
    {
        return value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "UDS 响应超时必须大于零。");
    }
}
