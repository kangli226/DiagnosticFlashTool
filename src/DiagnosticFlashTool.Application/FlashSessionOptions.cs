using DiagnosticFlashTool.Core.Can;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Util;
using DiagnosticFlashTool.Infrastructure.Security;

namespace DiagnosticFlashTool.Application;

/// <summary>
/// Complete input for one ECU flashing session. Product-specific defaults
/// live in an EcuProductProfile; this object contains the user-selected
/// runtime values and is never written to resources/configs.
/// </summary>
public sealed class FlashSessionOptions
{
    public BootConfig BootConfig { get; init; } = new();
    public ProjectConfigEntry Project { get; init; } = new();
    public CanDeviceOptions Device { get; init; } = new();
    public DiagnosticTransportOptions Transport { get; init; } = new();
    public UdsTimingOptions Timing { get; init; } = new();
    public string? DriverFilePath { get; init; }
    public IReadOnlyList<string> ApplicationFilePaths { get; init; } = [];
    public bool RequireDriverFile { get; init; }
    public bool RequireApplicationFile { get; init; } = true;
    public uint DriverFallbackAddress { get; init; }
    public uint ApplicationFallbackAddress { get; init; }
    public SeedKeyDllOptions? SeedKeyDll { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Project.ProjectName))
        {
            throw new ArgumentException("Project name is required.", nameof(Project));
        }

        if (BootConfig.Flow.Count == 0)
        {
            throw new InvalidOperationException($"BOOT config has no flow steps: {BootConfig.Name}");
        }

        Timing.Validate();
        ValidateFlow();
        if (Device.BaudRate == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Device), "CAN baud rate must be greater than zero.");
        }

        if (Transport.PhysicalRequestId == 0 || Transport.ResponseId == 0)
        {
            throw new ArgumentException("Physical request and response CAN IDs are required.", nameof(Transport));
        }

        if (Transport.Channel != Device.Channel)
        {
            throw new ArgumentException("Diagnostic transport channel must match the connected CAN channel.", nameof(Transport));
        }

        if (RequireDriverFile && string.IsNullOrWhiteSpace(DriverFilePath))
        {
            throw new InvalidOperationException("The required Driver firmware file is not configured.");
        }

        if (RequireApplicationFile && !ApplicationFilePaths.Any(path => !string.IsNullOrWhiteSpace(path)))
        {
            throw new InvalidOperationException("At least one required Application firmware file must be configured.");
        }

        if (ApplicationFilePaths.Count(path => string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase)) > 1)
        {
            throw new InvalidOperationException("Multiple BIN application files require per-file addresses and cannot share one fallback address.");
        }

        var customAlgorithms = BootConfig.Flow
            .Select(step => step.SecurityAlgorithm)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, "AES128_OneFunc", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
        if (!string.Equals(Device.DeviceType, "Mock", StringComparison.OrdinalIgnoreCase)
            && customAlgorithms.Count > 0
            && SeedKeyDll is null)
        {
            throw new InvalidOperationException("A Seed&Key DLL is required by the selected BOOT flow.");
        }

        if (SeedKeyDll is not null
            && customAlgorithms.Any(name => !string.Equals(name, SeedKeyDll.AlgorithmName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The Seed&Key DLL algorithm name does not match the selected BOOT flow.");
        }
    }

    private void ValidateFlow()
    {
        foreach (var step in BootConfig.Flow)
        {
            var isDownload = string.Equals(step.StepType, "DownloadDriver", StringComparison.OrdinalIgnoreCase)
                || string.Equals(step.StepType, "DownloadApplication", StringComparison.OrdinalIgnoreCase);
            if (!isDownload && !HexUtil.TryParseByte(step.Service, out _))
            {
                throw new InvalidOperationException($"Flash step {step.Id} has neither a supported download type nor a valid UDS service.");
            }

            if (!string.IsNullOrWhiteSpace(step.EraseRoutine)
                || !string.IsNullOrWhiteSpace(step.CrcAlgorithm)
                || step.Receive.Count > 0
                || step.Verify.Count > 0)
            {
                throw new NotSupportedException(
                    $"Flash step {step.Id} uses erase/CRC/receive verification metadata that is not implemented by the dedicated session. Define explicit UDS routine steps before production use.");
            }
        }
    }
}
