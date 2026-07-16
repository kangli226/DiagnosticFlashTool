using System.Text.Json.Serialization;

namespace DiagnosticFlashTool.Core.Configuration;

public sealed class BootConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("transport")]
    public BootTransportConfig? Transport { get; set; }

    [JsonPropertyName("scripts")]
    public List<FlowScriptConfig> Scripts { get; set; } = [];

    [JsonPropertyName("flow")]
    public List<FlashStepConfig> Flow { get; set; } = [];

    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;
}

public sealed class BootTransportConfig
{
    [JsonPropertyName("physicalRequestId")]
    public string? PhysicalRequestId { get; set; }

    [JsonPropertyName("functionalRequestId")]
    public string? FunctionalRequestId { get; set; }

    [JsonPropertyName("responseAddressId")]
    public string? ResponseAddressId { get; set; }

    [JsonPropertyName("baudRate")]
    public string? BaudRate { get; set; }
}

public sealed class FlowScriptConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("parameterNames")]
    public List<string> ParameterNames { get; set; } = [];
}

public sealed class FlashStepConfig
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("stepType")]
    public string? StepType { get; set; }

    [JsonPropertyName("service")]
    public string? Service { get; set; }

    [JsonPropertyName("subService")]
    public string? SubService { get; set; }

    [JsonPropertyName("extend")]
    public JsonStringList Extend { get; set; } = [];

    [JsonPropertyName("receive")]
    public Dictionary<string, string> Receive { get; set; } = [];

    [JsonPropertyName("verify")]
    public JsonStringList Verify { get; set; } = [];

    [JsonPropertyName("blockSize")]
    public string? BlockSize { get; set; }

    [JsonPropertyName("timeoutMs")]
    public string? TimeoutMs { get; set; }

    [JsonPropertyName("pendingTimeoutMs")]
    public string? PendingTimeoutMs { get; set; }

    [JsonPropertyName("testerPresentIntervalMs")]
    public string? TesterPresentIntervalMs { get; set; }

    [JsonPropertyName("eraseRoutine")]
    public string? EraseRoutine { get; set; }

    [JsonPropertyName("addressing")]
    public string? AddressingMode { get; set; }

    [JsonPropertyName("securityAlgorithm")]
    public string? SecurityAlgorithm { get; set; }

    [JsonPropertyName("crcAlgorithm")]
    public string? CrcAlgorithm { get; set; }

    [JsonPropertyName("algorithmParams")]
    public JsonStringList AlgorithmParams { get; set; } = [];
}
