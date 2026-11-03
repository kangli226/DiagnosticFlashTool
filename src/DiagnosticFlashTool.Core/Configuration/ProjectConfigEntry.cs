using System.Text.Json.Serialization;

namespace DiagnosticFlashTool.Core.Configuration;

public sealed class ProjectConfigEntry
{
    [JsonPropertyName("ProjectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("projectNo")]
    public int ProjectNo { get; set; }

    [JsonPropertyName("CanProtocol")]
    public string CanProtocol { get; set; } = "CAN";

    [JsonPropertyName("BaudRate")]
    public string BaudRate { get; set; } = "500K";

    [JsonPropertyName("PhysicalRequestId")]
    public string PhysicalRequestId { get; set; } = "0x18DA5535";

    [JsonPropertyName("FunctionalRequestId")]
    public string FunctionalRequestId { get; set; } = "0x18DA55FF";

    [JsonPropertyName("ResponseAddressId")]
    public string ResponseAddressId { get; set; } = "0x18DA3555";

    [JsonPropertyName("BootConfigFile")]
    public string BootConfigFile { get; set; } = string.Empty;

    [JsonPropertyName("DriveFilePath")]
    public string DriveFilePath { get; set; } = string.Empty;

    [JsonPropertyName("FlashFilePath")]
    public string FlashFilePath { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsDriverFileConfigured => !string.IsNullOrWhiteSpace(DriveFilePath);

    [JsonIgnore]
    public bool IsDriverFileAvailable => IsDriverFileConfigured && File.Exists(DriveFilePath);

    [JsonIgnore]
    public string DriverFileStatusIcon => GetFileStatusIcon(DriveFilePath);

    [JsonIgnore]
    public string DriverFileStatusText => GetFileStatusText(DriveFilePath, "驱动");

    [JsonIgnore]
    public bool IsApplicationFileConfigured => !string.IsNullOrWhiteSpace(FlashFilePath);

    [JsonIgnore]
    public bool IsApplicationFileAvailable => IsApplicationFileConfigured && File.Exists(FlashFilePath);

    [JsonIgnore]
    public string ApplicationFileStatusIcon => GetFileStatusIcon(FlashFilePath);

    [JsonIgnore]
    public string ApplicationFileStatusText => GetFileStatusText(FlashFilePath, "应用");

    public override string ToString() => ProjectName;

    private static string GetFileStatusIcon(string? path) => string.IsNullOrWhiteSpace(path)
        ? "—"
        : File.Exists(path) ? "✓" : "×";

    private static string GetFileStatusText(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return $"{label} 文件未配置";
        }

        return File.Exists(path)
            ? $"{label} 文件可用：{path}"
            : $"{label} 文件不存在：{path}";
    }
}
