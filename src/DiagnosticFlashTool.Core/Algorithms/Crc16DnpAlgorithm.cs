namespace DiagnosticFlashTool.Core.Algorithms;

public sealed class Crc16DnpAlgorithm : ICrcAlgorithm
{
    public const string AlgorithmName = "CRC16_DNP";

    public string Name => AlgorithmName;

    public int WidthBits => 16;

    public uint Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0x0000;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 0x0001) != 0
                    ? (crc >> 1) ^ 0xA6BC
                    : crc >> 1);
            }
        }

        return (ushort)~crc;
    }
}
