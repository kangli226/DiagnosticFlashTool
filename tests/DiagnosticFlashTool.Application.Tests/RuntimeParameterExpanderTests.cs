using DiagnosticFlashTool.Core.Flashing;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

public sealed class RuntimeParameterExpanderTests
{
    [Fact]
    public void Expand_SupportsBigAndLittleEndianIntegerFormats()
    {
        var variables = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["application.crc"] = 0x12345678,
            ["application.start"] = 0x00400000,
            ["application.length"] = 0x0102
        };

        var bytes = RuntimeParameterExpander.Expand(
            [
                "0xF2, 0x04",
                "${application.start:u32be}",
                "${application.length:u16le}",
                "${application.crc:u32le}"
            ],
            variables,
            out var references);

        Assert.Equal(
            new byte[]
            {
                0xF2, 0x04,
                0x00, 0x40, 0x00, 0x00,
                0x02, 0x01,
                0x78, 0x56, 0x34, 0x12
            },
            bytes);
        Assert.Contains("application.crc", references);
    }

    [Fact]
    public void Expand_RejectsMissingRuntimeVariable()
    {
        Assert.Throws<KeyNotFoundException>(() => RuntimeParameterExpander.Expand(
            ["${driver.crc:u16be}"],
            new Dictionary<string, ulong>(),
            out _));
    }
}
