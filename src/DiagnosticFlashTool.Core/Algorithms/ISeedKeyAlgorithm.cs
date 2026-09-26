namespace DiagnosticFlashTool.Core.Algorithms;

public interface ISeedKeyAlgorithm
{
    string Name { get; }

    /// <summary>
    /// Computes the security key for <paramref name="seed"/>.
    /// </summary>
    /// <remarks>
    /// Asynchronous and cancellable so an implementation backed by a vendor DLL or a
    /// helper process can honour cancellation, instead of blocking the calling thread
    /// for the whole native call.
    /// </remarks>
    ValueTask<byte[]> ComputeKeyAsync(
        byte[] seed,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken = default);
}
