using DiagnosticFlashTool.Core.Algorithms;

namespace DiagnosticFlashTool.Application;

/// <summary>
/// Echo-through seed-key stub used when a session runs against the Mock device.
/// It lets an offline flow exercise the 0x27 request-seed / send-key pairing without
/// a vendor DLL, and it deliberately adds no security value.
/// </summary>
internal sealed class MockSeedKeyAlgorithm(string name) : ISeedKeyAlgorithm
{
    public string Name { get; } = name;

    public ValueTask<byte[]> ComputeKeyAsync(
        byte[] seed,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]>(seed.ToArray());
    }
}
