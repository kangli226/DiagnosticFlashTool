namespace DiagnosticFlashTool.Core.Firmware;

public sealed class S19Parser
{
    public FirmwareImage Load(string filePath, FirmwareImageKind kind)
    {
        var blocks = new List<FirmwareBlock>();

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith('S') || line.Length < 4)
            {
                throw new FormatException($"Invalid S-record line: {line}");
            }

            var type = line[1];
            if (type is not ('1' or '2' or '3'))
            {
                continue;
            }

            var bytes = ParseBytes(line[2..]);
            ValidateChecksum(bytes, line);
            var count = bytes[0];
            var addressLength = type switch
            {
                '1' => 2,
                '2' => 3,
                '3' => 4,
                _ => 0
            };

            uint address = 0;
            for (var i = 0; i < addressLength; i++)
            {
                address = (address << 8) | bytes[1 + i];
            }

            var dataLength = count - addressLength - 1;
            var data = bytes.Skip(1 + addressLength).Take(dataLength).ToArray();
            blocks.Add(new FirmwareBlock(address, data));
        }

        return new FirmwareImage
        {
            FilePath = filePath,
            Kind = kind,
            Blocks = Merge(blocks)
        };
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
            throw new FormatException("S-record byte string length must be even.");
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
        if (sum != 0xFF)
        {
            throw new FormatException($"Invalid S-record checksum: {line}");
        }
    }
}
