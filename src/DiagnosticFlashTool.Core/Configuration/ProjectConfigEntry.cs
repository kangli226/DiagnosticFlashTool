using System.Text.Json.Serialization;

namespace DiagnosticFlashTool.Core.Configuration;

public sealed class ProjectConfigEntry
{
    [JsonPropertyName("ProjectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("projectNo")]
    public int ProjectNo { get; set; }

    [JsonPropertyName("BaudRate")]
    public string BaudRate { get; set; } = "500K";

    [JsonPropertyName("PhysicalRequestId")]
    public string PhysicalRequestId { get; set; } = "0x18DA5535";

    [JsonPropertyName("FunctionalRequestId")]
    public string FunctionalRequestId { get; set; } = "0x18DA55FF";

    [JsonPropertyName("ResponseAddressId")]
    public string ResponseAddressId { get; set; } = "0x18DA3555";

    [JsonPropertyName("AppStartAddr")]
    public string? AppStartAddress { get; set; }

    [JsonPropertyName("AppEndAddr")]
    public string? AppEndAddress { get; set; }

    [JsonPropertyName("BootConfigFile")]
    public string BootConfigFile { get; set; } = string.Empty;

    [JsonPropertyName("DriveFilePath")]
    public string DriveFilePath { get; set; } = string.Empty;

    [JsonPropertyName("FlashFilePath")]
    public string FlashFilePath { get; set; } = string.Empty;

    public override string ToString() => ProjectName;
}
