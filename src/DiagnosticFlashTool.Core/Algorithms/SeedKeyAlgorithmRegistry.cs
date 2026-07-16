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

    public ISeedKeyAlgorithm Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return _algorithms["AES128_OneFunc"];
        }

        if (_algorithms.TryGetValue(name, out var algorithm))
        {
            return algorithm;
        }

        throw new NotSupportedException($"Seed-key algorithm is not registered: {name}");
    }
}
