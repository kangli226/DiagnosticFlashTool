namespace DiagnosticFlashTool.Core.Diagnostics;

/// <summary>
/// UDS client-side timing values. All values are expressed in milliseconds.
/// </summary>
public sealed class UdsTimingOptions
{
    public int P2ClientMs { get; init; } = 5000;
    public int P2StarClientMs { get; init; } = 5100;
    public int S3ClientMs { get; init; } = 5000;

    /// <summary>
    /// Optional upper bound for a sequence of 0x78 pending responses.
    /// </summary>
    public int PendingOverallTimeoutMs { get; init; } = 30_000;

    public TimeSpan P2ClientTimeout => TimeSpan.FromMilliseconds(ValidatePositive(P2ClientMs, nameof(P2ClientMs)));
    public TimeSpan P2StarClientTimeout => TimeSpan.FromMilliseconds(ValidatePositive(P2StarClientMs, nameof(P2StarClientMs)));
    public TimeSpan? PendingOverallTimeout => PendingOverallTimeoutMs <= 0
        ? null
        : TimeSpan.FromMilliseconds(PendingOverallTimeoutMs);

    public UdsTimingOptions Validate()
    {
        _ = P2ClientTimeout;
        _ = P2StarClientTimeout;
        if (S3ClientMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(S3ClientMs), "S3 client interval cannot be negative.");
        }

        if (PendingOverallTimeoutMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PendingOverallTimeoutMs), "Pending overall timeout cannot be negative.");
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
            PendingOverallTimeoutMs = Math.Max(1, p2StarMs)
        };
    }

    private static int ValidatePositive(int value, string parameterName)
    {
        return value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "UDS response timeout must be greater than zero.");
    }
}
