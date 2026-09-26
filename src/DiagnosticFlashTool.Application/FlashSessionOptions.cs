using DiagnosticFlashTool.Core.Algorithms;
using DiagnosticFlashTool.Core.Can;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Flashing;
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

        // The flow is the specification: a download step means that firmware has to
        // exist. Without this a flow could declare a download, supply no file, and still
        // report success.
        if (RequiresDownload("DownloadDriver") && string.IsNullOrWhiteSpace(DriverFilePath))
        {
            throw new InvalidOperationException(
                "BOOT 流程包含 DownloadDriver 步骤，但没有配置 Driver 固件文件。");
        }

        if (RequiresDownload("DownloadApplication")
            && !ApplicationFilePaths.Any(path => !string.IsNullOrWhiteSpace(path)))
        {
            throw new InvalidOperationException(
                "BOOT 流程包含 DownloadApplication 步骤，但没有配置 Application 固件文件。");
        }

        // Runs last so the targeted messages above win over the generic flow validation.
        ValidateFlow();
    }

    private bool RequiresDownload(string stepType)
    {
        return BootConfig.Flow.Any(step =>
            string.Equals(step.StepType, stepType, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsMockDevice =>
        string.Equals(Device.DeviceType, "Mock", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Algorithms the session can resolve for this flow. Mirrors what
    /// <see cref="FlashSessionService"/> registers at flash time, so validation and
    /// execution cannot disagree about whether an algorithm is available.
    /// </summary>
    public SeedKeyAlgorithmRegistry CreateAlgorithmRegistry()
    {
        var algorithms = new SeedKeyAlgorithmRegistry();
        if (SeedKeyDll is not null)
        {
            algorithms.Register(new DllSeedKeyAlgorithm(SeedKeyDll));
            return algorithms;
        }

        if (!IsMockDevice)
        {
            return algorithms;
        }

        // Offline runs have no vendor DLL, so every algorithm the flow names is stubbed.
        var declared = BootConfig.Flow
            .Select(step => step.SecurityAlgorithm)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>();

        foreach (var name in declared)
        {
            algorithms.Register(new MockSeedKeyAlgorithm(name));
        }

        return algorithms;
    }

    private void ValidateFlow()
    {
        var issues = FlashFlowValidator.Validate(BootConfig, CreateAlgorithmRegistry());
        var errors = issues.Where(issue => issue.Kind == FlashFlowIssueKind.Error).ToList();
        if (errors.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "BOOT 流程校验未通过：" + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(issue => "  - " + issue)));
    }
}
