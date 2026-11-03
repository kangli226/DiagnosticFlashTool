using System.Text.RegularExpressions;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Core.Flashing;

public static partial class RuntimeParameterExpander
{
    private static readonly Regex AnyPlaceholderRegex = new(
        @"\$\{[^}]+\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [GeneratedRegex(
        @"\$\{(?<name>(?:driver|application)\.(?:crc|start|length)):(?<format>u16be|u16le|u32be|u32le)\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SupportedPlaceholderRegex();

    public static byte[] Expand(
        IEnumerable<string> values,
        IReadOnlyDictionary<string, ulong> variables,
        out IReadOnlySet<string> referencedVariables)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(variables);

        var result = new List<byte>();
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            ExpandValue(value, variables, result, references);
        }

        referencedVariables = references;
        return [.. result];
    }

    public static IReadOnlyList<string> GetReferencedVariables(IEnumerable<string> values)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            foreach (Match match in SupportedPlaceholderRegex().Matches(value ?? string.Empty))
            {
                references.Add(match.Groups["name"].Value.ToLowerInvariant());
            }
        }

        return [.. references];
    }

    public static bool HasUnsupportedPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var supported = SupportedPlaceholderRegex().Replace(value, string.Empty);
        return supported.Contains("${", StringComparison.Ordinal)
            || AnyPlaceholderRegex.IsMatch(supported);
    }

    public static bool IsStandalonePlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = SupportedPlaceholderRegex().Match(value.Trim());
        return match.Success && match.Length == value.Trim().Length;
    }

    private static void ExpandValue(
        string? value,
        IReadOnlyDictionary<string, ulong> variables,
        List<byte> result,
        HashSet<string> references)
    {
        var text = value ?? string.Empty;
        var position = 0;
        foreach (Match match in SupportedPlaceholderRegex().Matches(text))
        {
            AddLiteral(text[position..match.Index], result);

            var name = match.Groups["name"].Value.ToLowerInvariant();
            if (!variables.TryGetValue(name, out var variableValue))
            {
                throw new KeyNotFoundException($"运行时变量尚未生成：{name}");
            }

            AddFormatted(variableValue, match.Groups["format"].Value, result);
            references.Add(name);
            position = match.Index + match.Length;
        }

        var remainder = text[position..];
        if (HasUnsupportedPlaceholder(remainder))
        {
            throw new FormatException($"包含不支持的运行时占位符：{value}");
        }

        AddLiteral(remainder, result);
    }

    private static void AddLiteral(string value, List<byte> result)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.All(character => char.IsWhiteSpace(character) || character is ',' or ';'))
        {
            return;
        }

        result.AddRange(HexUtil.ParseBytes(value.Replace(';', ',')));
    }

    private static void AddFormatted(ulong value, string format, List<byte> result)
    {
        switch (format.ToLowerInvariant())
        {
            case "u16be":
                EnsureFits(value, ushort.MaxValue, format);
                result.Add((byte)(value >> 8));
                result.Add((byte)value);
                break;
            case "u16le":
                EnsureFits(value, ushort.MaxValue, format);
                result.Add((byte)value);
                result.Add((byte)(value >> 8));
                break;
            case "u32be":
                EnsureFits(value, uint.MaxValue, format);
                result.Add((byte)(value >> 24));
                result.Add((byte)(value >> 16));
                result.Add((byte)(value >> 8));
                result.Add((byte)value);
                break;
            case "u32le":
                EnsureFits(value, uint.MaxValue, format);
                result.Add((byte)value);
                result.Add((byte)(value >> 8));
                result.Add((byte)(value >> 16));
                result.Add((byte)(value >> 24));
                break;
            default:
                throw new FormatException($"不支持的运行时变量格式：{format}");
        }
    }

    private static void EnsureFits(ulong value, ulong maximum, string format)
    {
        if (value > maximum)
        {
            throw new OverflowException($"运行时变量值 0x{value:X} 超出 {format} 可表示范围。");
        }
    }
}
