using DiagnosticFlashTool.Core.Algorithms;

namespace DiagnosticFlashTool.Infrastructure.Security;

/// <summary>
/// Seed&amp;Key registry adapter backed by a vendor DLL through SeedKeyWorker.
/// </summary>
public sealed class DllSeedKeyAlgorithm : ISeedKeyAlgorithm
{
    private readonly SeedKeyDllOptions _options;
    private readonly SeedKeyWorkerClient _workerClient;

    public DllSeedKeyAlgorithm(
        SeedKeyDllOptions options,
        SeedKeyWorkerClient? workerClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _workerClient = workerClient ?? new SeedKeyWorkerClient();
    }

    public string Name => _options.AlgorithmName;

    public byte[] ComputeKey(byte[] seed, IReadOnlyList<string> parameters)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(parameters);

        // ISeedKeyAlgorithm is intentionally synchronous for compatibility
        // with the existing flow executor. The worker itself remains async
        // and is never loaded into the host process.
        return _workerClient.ComputeKeyAsync(_options, seed).GetAwaiter().GetResult();
    }
}
