using System.IO;
using System.Globalization;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.App.Utilities;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed class ProjectEditorViewModel : ObservableObject
{
    private string _projectName = string.Empty;
    private string _projectNoText = "1";
    private string _canProtocol = "CAN";
    private string _baudRate = "500K";
    private string _physicalRequestId = "0x18DA5535";
    private string _functionalRequestId = "0x18DA55FF";
    private string _responseAddressId = "0x18DA3555";
    private string _bootConfigFile = string.Empty;
    private string _driverFilePath = string.Empty;
    private string _applicationFilePath = string.Empty;
    private bool _isDriverFileRequired;
    private bool _isApplicationFileRequired;

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

    public string ProjectNoText
    {
        get => _projectNoText;
        set => SetProperty(ref _projectNoText, value);
    }

    public string CanProtocol
    {
        get => _canProtocol;
        set => SetProperty(ref _canProtocol, value);
    }

    public string BaudRate
    {
        get => _baudRate;
        set => SetProperty(ref _baudRate, value);
    }

    public string PhysicalRequestId
    {
        get => _physicalRequestId;
        set => SetProperty(ref _physicalRequestId, value);
    }

    public string FunctionalRequestId
    {
        get => _functionalRequestId;
        set => SetProperty(ref _functionalRequestId, value);
    }

    public string ResponseAddressId
    {
        get => _responseAddressId;
        set => SetProperty(ref _responseAddressId, value);
    }

    public string BootConfigFile
    {
        get => _bootConfigFile;
        set => SetProperty(ref _bootConfigFile, value);
    }

    public string DriverFilePath
    {
        get => _driverFilePath;
        set
        {
            if (SetProperty(ref _driverFilePath, value))
            {
                OnPropertyChanged(nameof(DriverFileDisplay));
            }
        }
    }

    public string ApplicationFilePath
    {
        get => _applicationFilePath;
        set
        {
            if (SetProperty(ref _applicationFilePath, value))
            {
                OnPropertyChanged(nameof(ApplicationFileDisplay));
            }
        }
    }

    public string DriverFileDisplay
    {
        get => FilePathDisplayFormatter.GetFileName(DriverFilePath);
        set => DriverFilePath = UpdateFileName(DriverFilePath, value);
    }

    public string ApplicationFileDisplay
    {
        get => FilePathDisplayFormatter.GetFileName(ApplicationFilePath);
        set => ApplicationFilePath = UpdateFileName(ApplicationFilePath, value);
    }

    public bool IsDriverFileRequired
    {
        get => _isDriverFileRequired;
        private set => SetProperty(ref _isDriverFileRequired, value);
    }

    public bool IsApplicationFileRequired
    {
        get => _isApplicationFileRequired;
        private set => SetProperty(ref _isApplicationFileRequired, value);
    }

    public void SetFirmwareRequirements(bool requiresDriverFile, bool requiresApplicationFile)
    {
        IsDriverFileRequired = requiresDriverFile;
        IsApplicationFileRequired = requiresApplicationFile;
    }

    public void BeginNew(string bootConfigFile, int projectNo)
    {
        ProjectName = string.Empty;
        ProjectNoText = projectNo.ToString(CultureInfo.InvariantCulture);
        CanProtocol = "CAN";
        BaudRate = "500K";
        PhysicalRequestId = "0x18DA5535";
        FunctionalRequestId = "0x18DA55FF";
        ResponseAddressId = "0x18DA3555";
        BootConfigFile = bootConfigFile;
        DriverFilePath = string.Empty;
        ApplicationFilePath = string.Empty;
    }

    public void Load(ProjectConfigEntry project)
    {
        ProjectName = project.ProjectName;
        ProjectNoText = project.ProjectNo.ToString(CultureInfo.InvariantCulture);
        CanProtocol = project.CanProtocol;
        BaudRate = project.BaudRate;
        PhysicalRequestId = project.PhysicalRequestId;
        FunctionalRequestId = project.FunctionalRequestId;
        ResponseAddressId = project.ResponseAddressId;
        BootConfigFile = project.BootConfigFile;
        DriverFilePath = project.DriveFilePath;
        ApplicationFilePath = project.FlashFilePath;
    }

    public ProjectConfigEntry? CreateProject(out string validationMessage)
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            validationMessage = "项目名称不能为空。";
            return null;
        }

        if (!TryParseProjectNo(ProjectNoText, out var projectNo) || projectNo <= 0)
        {
            validationMessage = "项目码必须是大于 0 的十进制或十六进制整数。";
            return null;
        }

        if (!string.Equals(CanProtocol, "CAN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(CanProtocol, "CANFD", StringComparison.OrdinalIgnoreCase))
        {
            validationMessage = "CAN 协议只能选择 CAN 或 CANFD。";
            return null;
        }

        if (string.IsNullOrWhiteSpace(PhysicalRequestId)
            || string.IsNullOrWhiteSpace(FunctionalRequestId)
            || string.IsNullOrWhiteSpace(ResponseAddressId))
        {
            validationMessage = "请完整填写物理请求、功能请求和响应地址。";
            return null;
        }

        if (string.IsNullOrWhiteSpace(BootConfigFile))
        {
            validationMessage = "请选择 BOOT 配置。";
            return null;
        }

        validationMessage = string.Empty;
        return new ProjectConfigEntry
        {
            ProjectName = ProjectName.Trim(),
            ProjectNo = projectNo,
            CanProtocol = string.Equals(CanProtocol, "CANFD", StringComparison.OrdinalIgnoreCase) ? "CANFD" : "CAN",
            BaudRate = BaudRate,
            PhysicalRequestId = PhysicalRequestId.Trim(),
            FunctionalRequestId = FunctionalRequestId.Trim(),
            ResponseAddressId = ResponseAddressId.Trim(),
            BootConfigFile = BootConfigFile,
            DriveFilePath = IsDriverFileRequired ? DriverFilePath.Trim() : string.Empty,
            FlashFilePath = IsApplicationFileRequired ? ApplicationFilePath.Trim() : string.Empty
        };
    }

    private static bool TryParseProjectNo(string value, out int projectNo)
    {
        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(
                text[2..],
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out projectNo);
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out projectNo);
    }

    private static string UpdateFileName(string currentPath, string? displayValue)
    {
        var value = displayValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            if (Path.IsPathRooted(value)
                || value.Contains(Path.DirectorySeparatorChar)
                || value.Contains(Path.AltDirectorySeparatorChar))
            {
                return value;
            }

            var directory = Path.GetDirectoryName(currentPath);
            return string.IsNullOrWhiteSpace(directory)
                ? value
                : Path.Combine(directory, value);
        }
        catch
        {
            return value;
        }
    }
}
