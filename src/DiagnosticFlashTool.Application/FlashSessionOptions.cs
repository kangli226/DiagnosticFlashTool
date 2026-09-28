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
            throw new ArgumentException("项目名称不能为空。", nameof(Project));
        }

        if (BootConfig.Flow.Count == 0)
        {
            throw new InvalidOperationException($"BOOT 配置没有流程步骤：{BootConfig.Name}");
        }

        Timing.Validate();
        if (Device.BaudRate == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Device), "CAN 波特率必须大于零。");
        }

        if (Transport.PhysicalRequestId == 0 || Transport.ResponseId == 0)
        {
            throw new ArgumentException("物理请求和响应 CAN ID 不能为空。", nameof(Transport));
        }

        if (Transport.Channel != Device.Channel)
        {
            throw new ArgumentException("诊断传输通道必须与已连接的 CAN 通道一致。", nameof(Transport));
        }

        if (RequireDriverFile && string.IsNullOrWhiteSpace(DriverFilePath))
        {
            throw new InvalidOperationException("未配置必需的驱动固件文件。");
        }

        if (RequireApplicationFile && !ApplicationFilePaths.Any(path => !string.IsNullOrWhiteSpace(path)))
        {
            throw new InvalidOperationException("至少需要配置一个应用固件文件。");
        }

        if (ApplicationFilePaths.Count(path => string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase)) > 1)
        {
            throw new InvalidOperationException("多个 BIN 应用固件必须分别配置地址，不能共用同一个回退地址。");
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
            throw new InvalidOperationException("当前 BOOT 流程需要配置 Seed/Key DLL。");
        }

        if (SeedKeyDll is not null
            && customAlgorithms.Any(name => !string.Equals(name, SeedKeyDll.AlgorithmName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Seed/Key DLL 的算法名称与当前 BOOT 流程不匹配。");
        }

        // The flow is the specification: a download step means that firmware has to
        // exist. Without this a flow could declare a download, supply no file, and still
        // report success.
        if (RequiresDownload(FlashStepKind.DownloadDriver) && string.IsNullOrWhiteSpace(DriverFilePath))
        {
            throw new InvalidOperationException(
                "BOOT 流程包含 DownloadDriver 步骤，但没有配置 Driver 固件文件。");
        }

        if (RequiresDownload(FlashStepKind.DownloadApplication)
            && !ApplicationFilePaths.Any(path => !string.IsNullOrWhiteSpace(path)))
        {
            throw new InvalidOperationException(
                "BOOT 流程包含 DownloadApplication 步骤，但没有配置 Application 固件文件。");
        }

        // Runs last so the targeted messages above win over the generic flow validation.
        ValidateFlow();
    }

    private bool RequiresDownload(FlashStepKind expectedKind)
    {
        return BootConfig.Flow.Any(step =>
            FlashStepTypes.TryParse(step.StepType, out var kind) && kind == expectedKind);
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
