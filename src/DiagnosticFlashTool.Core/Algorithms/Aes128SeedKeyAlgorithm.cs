using System.Security.Cryptography;

namespace DiagnosticFlashTool.Core.Algorithms;

public sealed class Aes128SeedKeyAlgorithm : ISeedKeyAlgorithm
{
    private static readonly byte[] DefaultKey =
    [
        0x00, 0x01, 0x02, 0x03,
        0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B,
        0x0C, 0x0D, 0x0E, 0x0F
    ];

    public string Name => "AES128_OneFunc";

    public byte[] ComputeKey(byte[] seed, IReadOnlyList<string> parameters)
    {
        if (seed.Length == 0 || seed.Length % 16 != 0)
        {
            throw new ArgumentException("AES128 seed length must be a non-zero multiple of 16 bytes.", nameof(seed));
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = DefaultKey;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(seed, 0, seed.Length);
    }
}
