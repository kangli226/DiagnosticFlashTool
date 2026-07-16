using System.Globalization;

namespace DiagnosticFlashTool.Core.Util;

public static class HexUtil
{
    public static uint ParseUInt32(string? value, uint fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return uint.Parse(value, CultureInfo.InvariantCulture);
    }

    public static int ParseInt(string? value, int fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public static bool TryParseByte(string? value, out byte result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return byte.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        if (value.StartsWith("0X", StringComparison.Ordinal))
        {
            return byte.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        return byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    public static byte[] ParseBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var text = value.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal);
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length > 1)
        {
            return parts.Select(part => Convert.ToByte(part, 16)).ToArray();
        }

        if (text.Length % 2 != 0)
        {
            text = "0" + text;
        }

        var bytes = new byte[text.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    public static string ToHex(IEnumerable<byte> bytes)
    {
        return string.Join(" ", bytes.Select(x => x.ToString("X2")));
    }

    public static uint ParseBaudRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 500_000;
        }

        value = value.Trim().ToUpperInvariant();
        if (value.EndsWith('K'))
        {
            return uint.Parse(value[..^1], CultureInfo.InvariantCulture) * 1000;
        }

        return uint.Parse(value, CultureInfo.InvariantCulture);
    }
}
