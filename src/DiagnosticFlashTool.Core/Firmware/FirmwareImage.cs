namespace DiagnosticFlashTool.Core.Firmware;

public sealed class FirmwareImage
{
    public string FilePath { get; init; } = string.Empty;
    public FirmwareImageKind Kind { get; init; }
    public List<FirmwareBlock> Blocks { get; init; } = [];

    public int Length => Blocks.Sum(block => block.Data.Length);
    public uint StartAddress => Blocks.Count == 0 ? 0 : Blocks.Min(block => block.Address);

    public byte[] ToContiguousBytes(byte fill = 0xFF)
    {
        if (Blocks.Count == 0)
        {
            return [];
        }

        var start = Blocks.Min(block => block.Address);
        var end = Blocks.Max(block => block.Address + (uint)block.Data.Length);
        var data = Enumerable.Repeat(fill, checked((int)(end - start))).ToArray();

        foreach (var block in Blocks)
        {
            Buffer.BlockCopy(block.Data, 0, data, checked((int)(block.Address - start)), block.Data.Length);
        }

        return data;
    }
}

public sealed record FirmwareBlock(uint Address, byte[] Data);

public enum FirmwareImageKind
{
    Driver,
    Application
}

public sealed class FirmwareSet
{
    public FirmwareImage? Driver { get; init; }
    public FirmwareImage? Application { get; init; }
}
