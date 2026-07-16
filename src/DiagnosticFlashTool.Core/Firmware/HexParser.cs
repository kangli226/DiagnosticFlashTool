namespace DiagnosticFlashTool.Core.Firmware;

public sealed class HexParser
{
    public FirmwareImage Load(string filePath, FirmwareImageKind kind)
    {
        var blocks = new List<FirmwareBlock>();
        uint upperAddress = 0;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith(":", StringComparison.Ordinal))
            {
                throw new FormatException($"Invalid Intel HEX line: {line}");
            }

            var bytes = ParseBytes(line[1..]);
            ValidateChecksum(bytes, line);
            var length = bytes[0];
            var offset = (ushort)((bytes[1] << 8) | bytes[2]);
            var recordType = bytes[3];
            var data = bytes.Skip(4).Take(length).ToArray();

            switch (recordType)
            {
                case 0x00:
                    blocks.Add(new FirmwareBlock(upperAddress + offset, data));
                    break;
                case 0x01:
                    return new FirmwareImage { FilePath = filePath, Kind = kind, Blocks = Merge(blocks) };
                case 0x02:
                    upperAddress = (uint)(((data[0] << 8) | data[1]) << 4);
                    break;
                case 0x04:
                    upperAddress = (uint)(((data[0] << 8) | data[1]) << 16);
                    break;
            }
        }

        return new FirmwareImage { FilePath = filePath, Kind = kind, Blocks = Merge(blocks) };
    }

    private static List<FirmwareBlock> Merge(List<FirmwareBlock> blocks)
    {
        return blocks
            .OrderBy(block => block.Address)
            .Aggregate(new List<FirmwareBlock>(), (merged, block) =>
            {
                if (merged.Count == 0)
                {
                    merged.Add(block);
                    return merged;
                }

                var last = merged[^1];
                if (last.Address + last.Data.Length == block.Address)
                {
                    merged[^1] = last with { Data = [.. last.Data, .. block.Data] };
                }
                else
                {
                    merged.Add(block);
                }

                return merged;
            });
    }

    private static byte[] ParseBytes(string hex)
    {
        if (hex.Length % 2 != 0)
        {
            throw new FormatException("HEX byte string length must be even.");
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    private static void ValidateChecksum(byte[] bytes, string line)
    {
        var sum = bytes.Aggregate(0, (current, value) => (current + value) & 0xFF);
        if (sum != 0)
        {
            throw new FormatException($"Invalid Intel HEX checksum: {line}");
        }
    }
}
