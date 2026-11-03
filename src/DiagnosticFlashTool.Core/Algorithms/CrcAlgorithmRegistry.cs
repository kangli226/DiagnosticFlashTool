namespace DiagnosticFlashTool.Core.Algorithms;

public sealed class CrcAlgorithmRegistry
{
    private readonly Dictionary<string, ICrcAlgorithm> _algorithms = new(StringComparer.OrdinalIgnoreCase);

    public CrcAlgorithmRegistry()
    {
        Register(new Crc16DnpAlgorithm());
        Register(new Crc32Algorithm());
    }

    public IReadOnlyCollection<string> RegisteredNames => _algorithms.Keys.ToArray();

    public void Register(ICrcAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        _algorithms[algorithm.Name] = algorithm;
    }

    public bool IsRegistered(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && _algorithms.ContainsKey(name);
    }

    public ICrcAlgorithm Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && _algorithms.TryGetValue(name, out var algorithm))
        {
            return algorithm;
        }

        throw new NotSupportedException($"CRC 算法未注册：{name}");
    }
}
