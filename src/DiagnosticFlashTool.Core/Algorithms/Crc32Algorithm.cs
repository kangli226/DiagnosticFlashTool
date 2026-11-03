namespace DiagnosticFlashTool.Core.Algorithms;

public sealed class Crc32Algorithm : ICrcAlgorithm
{
    public const string AlgorithmName = "CRC32";

    public string Name => AlgorithmName;

    public int WidthBits => 32;

    public uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x00000001u) != 0
                    ? (crc >> 1) ^ 0xEDB88320u
                    : crc >> 1;
            }
        }

        return ~crc;
    }
}
