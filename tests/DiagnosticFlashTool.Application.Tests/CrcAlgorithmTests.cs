using System.Text;
using DiagnosticFlashTool.Core.Algorithms;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

public sealed class CrcAlgorithmTests
{
    private static readonly byte[] CheckData = Encoding.ASCII.GetBytes("123456789");

    [Fact]
    public void Crc16Dnp_UsesStandardCheckValue()
    {
        Assert.Equal(0xEA82u, new Crc16DnpAlgorithm().Compute(CheckData));
    }

    [Fact]
    public void Crc32_UsesStandardCheckValue()
    {
        Assert.Equal(0xCBF43926u, new Crc32Algorithm().Compute(CheckData));
    }
}
