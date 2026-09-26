using DiagnosticFlashTool.Core.Algorithms;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Core.Flashing;

public enum FlashFlowIssueKind
{
    /// <summary>Blocks flashing. The flow cannot be executed as declared.</summary>
    Error,

    /// <summary>Flashing proceeds, but the operator must be told what is not honoured.</summary>
    Warning
}

public sealed record FlashFlowIssue(FlashFlowIssueKind Kind, int StepId, string Message)
{
    public override string ToString() => StepId > 0 ? $"步骤 {StepId}：{Message}" : Message;
}

/// <summary>
/// Validates a BOOT flow against what <see cref="FlashFlowExecutor"/> can actually do.
/// </summary>
/// <remarks>
/// Two classes of problem are reported, and keeping them apart matters:
/// <list type="bullet">
/// <item>
/// A <see cref="FlashFlowIssueKind.Error"/> means the flow would fail or silently do
/// nothing. Flashing must be refused, because a "completed" flash would be a lie.
/// </item>
/// <item>
/// A <see cref="FlashFlowIssueKind.Warning"/> means the flow declares metadata the
/// executor does not consume yet (CRC, erase routine, receive/verify matchers).
/// The flow still runs, but the declaration is not honoured, so the operator has to
/// be told rather than left to assume the ECU was verified.
/// </item>
/// </list>
/// </remarks>
public static class FlashFlowValidator
{
    private const byte SecurityAccessService = 0x27;
    private const string DownloadDriverStepType = "DownloadDriver";
    private const string DownloadApplicationStepType = "DownloadApplication";

    public static IReadOnlyList<FlashFlowIssue> Validate(
        BootConfig bootConfig,
        SeedKeyAlgorithmRegistry? algorithms = null)
    {
        ArgumentNullException.ThrowIfNull(bootConfig);

        var issues = new List<FlashFlowIssue>();
        if (bootConfig.Flow.Count == 0)
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Error,
                0,
                $"BOOT 配置没有流程步骤：{bootConfig.Name}"));
            return issues;
        }

        var registry = algorithms ?? new SeedKeyAlgorithmRegistry();
        var seedStepId = 0;

        foreach (var step in bootConfig.Flow)
        {
            var isDownload = IsDownloadStep(step);

            if (!isDownload && !string.IsNullOrWhiteSpace(step.StepType))
            {
                issues.Add(new FlashFlowIssue(
                    FlashFlowIssueKind.Error,
                    step.Id,
                    $"未知的步骤类型 \"{step.StepType}\"，只能是 {DownloadDriverStepType} 或 {DownloadApplicationStepType}。"));
                continue;
            }

            if (!isDownload && !HexUtil.TryParseByte(step.Service, out _))
            {
                issues.Add(new FlashFlowIssue(
                    FlashFlowIssueKind.Error,
                    step.Id,
                    "既不是下载步骤，也没有可解析的 UDS 服务号。"));
                continue;
            }

            var securityIssues = ValidateSecuritySteps(step, registry, ref seedStepId);
            issues.AddRange(securityIssues);
            AddUnsupportedMetadataIssues(step, issues);
        }

        if (seedStepId > 0)
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Warning,
                seedStepId,
                "请求了 seed，但后续没有对应的 send-key 步骤。"));
        }

        return issues;
    }

    /// <summary>
    /// Validates the 0x27 request-seed / send-key pairing. ISO 14229 uses an odd
    /// sub-function to request the seed and the following even sub-function to send
    /// the key, so the two have to appear as an ordered pair within one flow.
    /// </summary>
    private static IEnumerable<FlashFlowIssue> ValidateSecuritySteps(
        FlashStepConfig step,
        SeedKeyAlgorithmRegistry registry,
        ref int seedStepId)
    {
        var issues = new List<FlashFlowIssue>();
        if (!HexUtil.TryParseByte(step.Service, out var serviceId) || serviceId != SecurityAccessService)
        {
            return issues;
        }

        if (!HexUtil.TryParseByte(step.SubService, out var subService))
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Error,
                step.Id,
                "安全访问（0x27）缺少可解析的子功能。"));
            return issues;
        }

        var isSendKey = subService % 2 == 0;
        if (!isSendKey)
        {
            seedStepId = step.Id;
            if (!string.IsNullOrWhiteSpace(step.SecurityAlgorithm))
            {
                issues.Add(new FlashFlowIssue(
                    FlashFlowIssueKind.Warning,
                    step.Id,
                    $"请求 seed 的步骤不需要 securityAlgorithm（当前为 \"{step.SecurityAlgorithm}\"），该设置会被忽略。"));
            }

            return issues;
        }

        if (seedStepId == 0)
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Error,
                step.Id,
                $"send-key（0x27 0x{subService:X2}）之前没有请求 seed 的步骤。"));
        }

        seedStepId = 0;

        if (string.IsNullOrWhiteSpace(step.SecurityAlgorithm))
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Error,
                step.Id,
                $"send-key（0x27 0x{subService:X2}）没有配置 securityAlgorithm，无法生成 key。"));
            return issues;
        }

        if (!registry.IsRegistered(step.SecurityAlgorithm))
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Error,
                step.Id,
                $"send-key 使用的算法 \"{step.SecurityAlgorithm}\" 未注册。已注册：{string.Join(", ", registry.RegisteredNames)}。"));
        }

        return issues;
    }

    /// <summary>
    /// Reports metadata the flow declares but <see cref="FlashFlowExecutor"/> does not consume.
    /// Erase/CRC/verify declarations must block, because an unverified flash that reports
    /// success is worse than a refused one.
    /// </summary>
    private static void AddUnsupportedMetadataIssues(FlashStepConfig step, List<FlashFlowIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(step.CrcAlgorithm))
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Error,
                step.Id,
                $"声明了 crcAlgorithm=\"{step.CrcAlgorithm}\"，但当前版本不执行 CRC 校验。请移除该声明，或改写为显式的 0x31 校验例程步骤。"));
        }

        if (!string.IsNullOrWhiteSpace(step.EraseRoutine))
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Error,
                step.Id,
                $"声明了 eraseRoutine=\"{step.EraseRoutine}\"，但当前版本不执行擦除例程。请移除该声明，或改写为显式的 0x31 擦除例程步骤。"));
        }

        if (step.Receive.Count > 0)
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Warning,
                step.Id,
                "声明了 receive 响应匹配规则，当前版本不执行响应校验。"));
        }

        if (step.Verify.Count > 0)
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Warning,
                step.Id,
                "声明了 verify 校验规则，当前版本不执行该校验。"));
        }

        if (!string.IsNullOrWhiteSpace(step.TesterPresentIntervalMs))
        {
            issues.Add(new FlashFlowIssue(
                FlashFlowIssueKind.Warning,
                step.Id,
                "声明了节点级 testerPresentIntervalMs，当前版本使用会话级周期，该设置会被忽略。"));
        }
    }

    public static bool IsDownloadStep(FlashStepConfig step)
    {
        return string.Equals(step.StepType, DownloadDriverStepType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(step.StepType, DownloadApplicationStepType, StringComparison.OrdinalIgnoreCase);
    }
}
