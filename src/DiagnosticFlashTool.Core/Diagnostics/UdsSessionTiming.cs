using System.Diagnostics.CodeAnalysis;

namespace DiagnosticFlashTool.Core.Diagnostics;

/// <summary>ECU 在 DiagnosticSessionControl（0x10）正响应中声明的服务端时序。</summary>
public sealed record UdsSessionTiming(int P2ServerMaxMs, int P2StarServerMaxMs)
{
    private const int P2NetworkMarginMs = 100;
    private const int P2StarNetworkMarginMs = 500;
    private const int MinimumP2ClientMs = 200;
    private const int MinimumP2StarClientMs = 1000;

    /// <summary>
    /// 从 0x50 正响应解析 P2Server_max 与 P2*Server_max。
    /// P2 的单位为 1 ms，P2* 的编码单位为 10 ms。
    /// </summary>
    public static bool TryParse(
        UdsResponse response,
        [NotNullWhen(true)] out UdsSessionTiming? timing)
    {
        ArgumentNullException.ThrowIfNull(response);
        timing = null;

        if (response.Request.Length < 2
            || response.Request[0] != 0x10
            || response.Payload.Length < 6
            || response.Payload[0] != 0x50
            || (response.Payload[1] & 0x7F) != (response.Request[1] & 0x7F))
        {
            return false;
        }

        var p2ServerMaxMs = (response.Payload[2] << 8) | response.Payload[3];
        var p2StarUnits = (response.Payload[4] << 8) | response.Payload[5];
        if (p2ServerMaxMs <= 0 || p2StarUnits <= 0)
        {
            return false;
        }

        timing = new UdsSessionTiming(p2ServerMaxMs, checked(p2StarUnits * 10));
        return true;
    }

    /// <summary>根据服务端上限和传输裕量生成本次诊断会话使用的客户端时序。</summary>
    public UdsTimingOptions CreateClientTiming(UdsTimingOptions configuredTiming)
    {
        ArgumentNullException.ThrowIfNull(configuredTiming);

        return new UdsTimingOptions
        {
            P2ClientMs = Math.Max(MinimumP2ClientMs, checked(P2ServerMaxMs + P2NetworkMarginMs)),
            P2StarClientMs = Math.Max(MinimumP2StarClientMs, checked(P2StarServerMaxMs + P2StarNetworkMarginMs)),
            S3ServerTimeoutMs = configuredTiming.S3ServerTimeoutMs,
            TesterPresentIntervalMs = configuredTiming.TesterPresentIntervalMs,
            PendingOverallTimeoutMs = configuredTiming.PendingOverallTimeoutMs
        }.Validate();
    }
}
