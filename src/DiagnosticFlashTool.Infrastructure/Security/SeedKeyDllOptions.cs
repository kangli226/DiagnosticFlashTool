using System.Text.Json.Serialization;

namespace DiagnosticFlashTool.Infrastructure.Security;

/// <summary>
/// Runtime settings for an ISO 22900/Vector-style Seed&amp;Key DLL.
/// The DLL is loaded by the worker process so the host application's bitness
/// does not constrain the algorithm binary.
/// </summary>
public sealed class SeedKeyDllOptions
{
    public string AlgorithmName { get; init; } = "ECU_DLL";
    public string DllPath { get; init; } = string.Empty;
    public string WorkerPath { get; init; } = "DiagnosticFlashTool.SeedKeyWorker.exe";
    public string EntryPoint { get; init; } = "GenerateKeyEx";
    public string CallingConvention { get; init; } = "cdecl";
    public uint SecurityLevel { get; init; } = 1;
    public string Variant { get; init; } = string.Empty;
    public string Options { get; init; } = string.Empty;
    public int TimeoutMs { get; init; } = 10_000;

    public SeedKeyDllOptions Validate()
    {
        if (string.IsNullOrWhiteSpace(DllPath))
        {
            throw new ArgumentException("Seed-key DLL path is required.", nameof(DllPath));
        }

        if (!File.Exists(DllPath))
        {
            throw new FileNotFoundException("Seed-key DLL was not found.", DllPath);
        }

        if (string.IsNullOrWhiteSpace(WorkerPath))
        {
            throw new ArgumentException("Seed-key worker path is required.", nameof(WorkerPath));
        }

        if (TimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeoutMs), "Seed-key worker timeout must be greater than zero.");
        }

        if (!string.Equals(CallingConvention, "cdecl", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(CallingConvention, "stdcall", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Calling convention must be cdecl or stdcall.", nameof(CallingConvention));
        }

        return this;
    }

    public static uint ParseSecurityLevel(string? value, uint fallback = 1)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            return uint.Parse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        }

        return uint.Parse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// One-line JSON request/response exchanged with SeedKeyWorker.
/// Hex strings are used for binary values to keep the protocol architecture
/// and encoding independent.
/// </summary>
public sealed class SeedKeyWorkerRequest
{
    [JsonPropertyName("dllPath")]
    public string DllPath { get; init; } = string.Empty;

    [JsonPropertyName("entryPoint")]
    public string EntryPoint { get; init; } = "GenerateKeyEx";

    [JsonPropertyName("callingConvention")]
    public string CallingConvention { get; init; } = "cdecl";

    [JsonPropertyName("seedHex")]
    public string SeedHex { get; init; } = string.Empty;

    [JsonPropertyName("securityLevel")]
    public uint SecurityLevel { get; init; }

    [JsonPropertyName("variant")]
    public string Variant { get; init; } = string.Empty;

    [JsonPropertyName("options")]
    public string Options { get; init; } = string.Empty;

    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; init; } = 10_000;
}

public sealed class SeedKeyWorkerResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("keyHex")]
    public string? KeyHex { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
