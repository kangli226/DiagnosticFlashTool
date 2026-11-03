using System.Text.Json.Serialization;
using DiagnosticFlashTool.Core.Firmware;
using DiagnosticFlashTool.Core.Flashing;

namespace DiagnosticFlashTool.Core.Configuration;

public sealed class ProjectConfigEntry
{
    private bool _isDriverFileRequired;
    private bool _isApplicationFileRequired;
    private bool _isDriverFileAvailable;
    private bool _isApplicationFileAvailable;
    private bool _isDriverFileError = true;
    private bool _isApplicationFileError = true;
    private string _driverFileStatusIcon = "×";
    private string _applicationFileStatusIcon = "×";
    private string _driverFileStatusText = "尚未检查 BOOT 配置和 Driver 文件。";
    private string _applicationFileStatusText = "尚未检查 BOOT 配置和 App 文件。";

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
    public bool IsDriverFileRequired => _isDriverFileRequired;

    [JsonIgnore]
    public bool IsDriverFileAvailable => _isDriverFileAvailable;

    [JsonIgnore]
    public bool IsDriverFileError => _isDriverFileError;

    [JsonIgnore]
    public string DriverFileStatusIcon => _driverFileStatusIcon;

    [JsonIgnore]
    public string DriverFileStatusText => _driverFileStatusText;

    [JsonIgnore]
    public bool IsApplicationFileConfigured => !string.IsNullOrWhiteSpace(FlashFilePath);

    [JsonIgnore]
    public bool IsApplicationFileRequired => _isApplicationFileRequired;

    [JsonIgnore]
    public bool IsApplicationFileAvailable => _isApplicationFileAvailable;

    [JsonIgnore]
    public bool IsApplicationFileError => _isApplicationFileError;

    [JsonIgnore]
    public string ApplicationFileStatusIcon => _applicationFileStatusIcon;

    [JsonIgnore]
    public string ApplicationFileStatusText => _applicationFileStatusText;

    public void RefreshFirmwareFileStatuses(
        BootConfig? bootConfig,
        FirmwareLoader firmwareLoader,
        string? bootConfigError = null)
    {
        if (bootConfig is null)
        {
            _isDriverFileRequired = false;
            _isApplicationFileRequired = false;
            var reason = string.IsNullOrWhiteSpace(bootConfigError)
                ? "BOOT 配置无法读取。"
                : bootConfigError.Trim();
            ApplyDriverStatus(FirmwareFileStatus.Error(
                $"BOOT 配置无效，无法判断 Driver 文件要求：{reason}"));
            ApplyApplicationStatus(FirmwareFileStatus.Error(
                $"BOOT 配置无效，无法判断 App 文件要求：{reason}"));
            return;
        }

        _isDriverFileRequired = RequiresDownloadStep(bootConfig, FlashStepKind.DownloadDriver);
        _isApplicationFileRequired = RequiresDownloadStep(bootConfig, FlashStepKind.DownloadApplication);

        ApplyDriverStatus(EvaluateFirmwareFile(
            _isDriverFileRequired,
            DriveFilePath,
            FirmwareImageKind.Driver,
            "Driver",
            firmwareLoader));
        ApplyApplicationStatus(EvaluateFirmwareFile(
            _isApplicationFileRequired,
            FlashFilePath,
            FirmwareImageKind.Application,
            "App",
            firmwareLoader));
    }

    public override string ToString() => ProjectName;

    private static bool RequiresDownloadStep(BootConfig bootConfig, FlashStepKind expectedKind)
    {
        return bootConfig.Flow.Any(step =>
            FlashStepTypes.TryParse(step.StepType, out var kind) && kind == expectedKind);
    }

    private static FirmwareFileStatus EvaluateFirmwareFile(
        bool isRequired,
        string? path,
        FirmwareImageKind kind,
        string label,
        FirmwareLoader firmwareLoader)
    {
        if (!isRequired)
        {
            return FirmwareFileStatus.NotRequired($"BOOT 配置未包含 {label} 下载步骤。");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return FirmwareFileStatus.Error($"BOOT 配置需要 {label} 文件，但项目未选择文件。");
        }

        if (!File.Exists(path))
        {
            return FirmwareFileStatus.Error($"{label} 文件不存在：{path}");
        }

        try
        {
            var image = firmwareLoader.Load(path, kind);
            return image.Length > 0
                ? FirmwareFileStatus.Available($"{label} 文件可用：{path}")
                : FirmwareFileStatus.Error($"{label} 文件无有效数据：{path}");
        }
        catch (Exception ex)
        {
            return FirmwareFileStatus.Error($"{label} 文件无效：{path}（{ex.Message}）");
        }
    }

    private void ApplyDriverStatus(FirmwareFileStatus status)
    {
        _isDriverFileAvailable = status.IsAvailable;
        _isDriverFileError = status.IsError;
        _driverFileStatusIcon = status.Icon;
        _driverFileStatusText = status.Text;
    }

    private void ApplyApplicationStatus(FirmwareFileStatus status)
    {
        _isApplicationFileAvailable = status.IsAvailable;
        _isApplicationFileError = status.IsError;
        _applicationFileStatusIcon = status.Icon;
        _applicationFileStatusText = status.Text;
    }

    private readonly record struct FirmwareFileStatus(
        bool IsAvailable,
        bool IsError,
        string Icon,
        string Text)
    {
        public static FirmwareFileStatus NotRequired(string text) => new(false, false, "-", text);

        public static FirmwareFileStatus Available(string text) => new(true, false, "✓", text);

        public static FirmwareFileStatus Error(string text) => new(false, true, "×", text);
    }
}
