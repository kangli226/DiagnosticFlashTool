using DiagnosticFlashTool.Core.Diagnostics;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

public sealed class UdsSessionTimingTests
{
    [Fact]
    public void TryParse_ConvertsDiagnosticSessionResponseToClientTiming()
    {
        var response = UdsResponse.FromPayload(
            [0x10, 0x02],
            [0x50, 0x02, 0x00, 0x32, 0x01, 0xF4]);

        Assert.True(UdsSessionTiming.TryParse(response, out var sessionTiming));
        Assert.NotNull(sessionTiming);
        Assert.Equal(50, sessionTiming.P2ServerMaxMs);
        Assert.Equal(5000, sessionTiming.P2StarServerMaxMs);

        var effective = sessionTiming.CreateClientTiming(new UdsTimingOptions
        {
            P2ClientMs = 1000,
            P2StarClientMs = 6000,
            S3ServerTimeoutMs = 5000,
            TesterPresentIntervalMs = 2000,
            PendingOverallTimeoutMs = 120_000
        });

        Assert.Equal(200, effective.P2ClientMs);
        Assert.Equal(5500, effective.P2StarClientMs);
        Assert.Equal(5000, effective.S3ServerTimeoutMs);
        Assert.Equal(2000, effective.TesterPresentIntervalMs);
        Assert.Equal(120_000, effective.PendingOverallTimeoutMs);
    }

    [Fact]
    public void TryParse_RejectsIncompleteOrInvalidTiming()
    {
        byte[][] invalidPayloads =
        [
            [0x50, 0x02],
            [0x50, 0x02, 0x00, 0x00, 0x01, 0xF4],
            [0x50, 0x03, 0x00, 0x32, 0x01, 0xF4]
        ];

        foreach (var payload in invalidPayloads)
        {
            var response = UdsResponse.FromPayload([0x10, 0x02], payload);

            Assert.False(UdsSessionTiming.TryParse(response, out var sessionTiming));
            Assert.Null(sessionTiming);
        }
    }

    [Fact]
    public void Validate_RejectsTesterPresentIntervalOutsideS3Window()
    {
        var timing = new UdsTimingOptions
        {
            S3ServerTimeoutMs = 5000,
            TesterPresentIntervalMs = 5000
        };

        Assert.Throws<ArgumentOutOfRangeException>(timing.Validate);
    }
}
