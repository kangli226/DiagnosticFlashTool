using System.Security.Cryptography;

namespace DiagnosticFlashTool.Core.Algorithms;

public sealed class Aes128SeedKeyAlgorithm : ISeedKeyAlgorithm
{
    public const string AlgorithmName = "AES128_OneFunc";

    private static readonly byte[] DefaultKey =
    [
        0x00, 0x01, 0x02, 0x03,
        0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B,
        0x0C, 0x0D, 0x0E, 0x0F
    ];

    public string Name => AlgorithmName;

    public ValueTask<byte[]> ComputeKeyAsync(
        byte[] seed,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        if (seed.Length == 0 || seed.Length % 16 != 0)
        {
            throw new ArgumentException("AES128 seed length must be a non-zero multiple of 16 bytes.", nameof(seed));
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = DefaultKey;

        using var encryptor = aes.CreateEncryptor();
        return new ValueTask<byte[]>(encryptor.TransformFinalBlock(seed, 0, seed.Length));
    }
}
