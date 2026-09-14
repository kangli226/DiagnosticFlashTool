using System.Text.Json.Serialization;
using DiagnosticFlashTool.Core.Diagnostics;

namespace DiagnosticFlashTool.Core.Configuration;

/// <summary>
/// Immutable product-owned defaults for a dedicated ECU flashing tool.
/// User editable values are stored separately in UserProductSettings.
/// </summary>
public sealed class EcuProductProfile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("integrationNote")]
    public string IntegrationNote { get; set; } = string.Empty;

    [JsonPropertyName("transport")]
    public ProductTransportConfig Transport { get; set; } = new();

    [JsonPropertyName("timing")]
    public ProductTimingConfig Timing { get; set; } = new();

    [JsonPropertyName("security")]
    public ProductSecurityConfig Security { get; set; } = new();

    [JsonPropertyName("flowFile")]
    public string FlowFile { get; set; } = string.Empty;

    [JsonPropertyName("firmware")]
    public List<ProductFirmwareSlotConfig> Firmware { get; set; } = [];

    public UdsTimingOptions CreateTimingOptions()
    {
        return new UdsTimingOptions
        {
            P2ClientMs = Timing.P2ClientMs,
            P2StarClientMs = Timing.P2StarClientMs,
            S3ClientMs = Timing.S3ClientMs,
            PendingOverallTimeoutMs = Timing.PendingOverallTimeoutMs
        }.Validate();
    }
}

public sealed class ProductTransportConfig
{
    [JsonPropertyName("bus")]
    public string Bus { get; set; } = "CAN";

    [JsonPropertyName("extendedFrame")]
    public bool ExtendedFrame { get; set; }

    [JsonPropertyName("physicalRequestId")]
    public string PhysicalRequestId { get; set; } = "0x18DA5535";

    [JsonPropertyName("functionalRequestId")]
    public string FunctionalRequestId { get; set; } = "0x18DA55FF";

    [JsonPropertyName("responseId")]
    public string ResponseId { get; set; } = "0x18DA3555";

    [JsonPropertyName("baudRate")]
    public uint BaudRate { get; set; } = 500_000;

    [JsonPropertyName("channel")]
    public uint Channel { get; set; }

    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = "Mock";

    [JsonPropertyName("deviceIndex")]
    public uint DeviceIndex { get; set; }

    [JsonPropertyName("flowControlBlockSize")]
    public int FlowControlBlockSize { get; set; } = 0x0A;

    [JsonPropertyName("flowControlStMinMs")]
    public int FlowControlStMinMs { get; set; } = 10;
}

public sealed class ProductTimingConfig
{
    [JsonPropertyName("p2ClientMs")]
    public int P2ClientMs { get; set; } = 5000;

    [JsonPropertyName("p2StarClientMs")]
    public int P2StarClientMs { get; set; } = 5100;

    [JsonPropertyName("s3ClientMs")]
    public int S3ClientMs { get; set; } = 5000;

    [JsonPropertyName("pendingOverallTimeoutMs")]
    public int PendingOverallTimeoutMs { get; set; } = 30_000;
}

public sealed class ProductSecurityConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "dll";

    [JsonPropertyName("algorithmName")]
    public string AlgorithmName { get; set; } = "ECU_DLL";

    [JsonPropertyName("entryPoint")]
    public string EntryPoint { get; set; } = "GenerateKeyEx";

    [JsonPropertyName("callingConvention")]
    public string CallingConvention { get; set; } = "cdecl";

    [JsonPropertyName("securityLevel")]
    public string SecurityLevel { get; set; } = "0x01";

    [JsonPropertyName("variant")]
    public string Variant { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public string Options { get; set; } = string.Empty;

    [JsonPropertyName("workerFile")]
    public string WorkerFile { get; set; } = "DiagnosticFlashTool.SeedKeyWorker.exe";
}

public sealed class ProductFirmwareSlotConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Application";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;

    [JsonPropertyName("fallbackAddress")]
    public string? FallbackAddress { get; set; }
}

public sealed class UserProductSettings
{
    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }

    [JsonPropertyName("deviceIndex")]
    public uint? DeviceIndex { get; set; }

    [JsonPropertyName("channel")]
    public uint? Channel { get; set; }

    [JsonPropertyName("baudRate")]
    public uint? BaudRate { get; set; }

    [JsonPropertyName("driverFilePath")]
    public string? DriverFilePath { get; set; }

    [JsonPropertyName("applicationFilePaths")]
    public List<string> ApplicationFilePaths { get; set; } = [];

    [JsonPropertyName("securityDllPath")]
    public string? SecurityDllPath { get; set; }

    [JsonPropertyName("algorithmName")]
    public string? AlgorithmName { get; set; }

    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    [JsonPropertyName("callingConvention")]
    public string? CallingConvention { get; set; }

    [JsonPropertyName("securityLevel")]
    public string? SecurityLevel { get; set; }

    [JsonPropertyName("variant")]
    public string? Variant { get; set; }

    [JsonPropertyName("options")]
    public string? Options { get; set; }

    [JsonPropertyName("p2ClientMs")]
    public int? P2ClientMs { get; set; }

    [JsonPropertyName("p2StarClientMs")]
    public int? P2StarClientMs { get; set; }

    [JsonPropertyName("s3ClientMs")]
    public int? S3ClientMs { get; set; }

    [JsonPropertyName("pendingOverallTimeoutMs")]
    public int? PendingOverallTimeoutMs { get; set; }
}
