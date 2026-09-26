namespace DiagnosticFlashTool.Core.Algorithms;

public sealed class SeedKeyAlgorithmRegistry
{
    private readonly Dictionary<string, ISeedKeyAlgorithm> _algorithms = new(StringComparer.OrdinalIgnoreCase);

    public SeedKeyAlgorithmRegistry()
    {
        Register(new Aes128SeedKeyAlgorithm());
    }

    public void Register(ISeedKeyAlgorithm algorithm)
    {
        _algorithms[algorithm.Name] = algorithm;
    }

    public IReadOnlyCollection<string> RegisteredNames => _algorithms.Keys.ToArray();

    public bool IsRegistered(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && _algorithms.ContainsKey(name);
    }

    public ISeedKeyAlgorithm Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return _algorithms.TryGetValue(Aes128SeedKeyAlgorithm.AlgorithmName, out var fallback)
                ? fallback
                : throw new NotSupportedException($"Seed-key algorithm is not registered: {name}");
        }

        if (_algorithms.TryGetValue(name, out var algorithm))
        {
            return algorithm;
        }

        throw new NotSupportedException($"Seed-key algorithm is not registered: {name}");
    }
}
