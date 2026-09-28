using DiagnosticFlashTool.Core.Algorithms;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Core.Flashing;

public enum FlashFlowIssueKind
{
    /// <summary>阻止刷写；当前流程无法按照配置内容可靠执行。</summary>
    Error,

    /// <summary>允许继续刷写，但必须告知操作人员哪些配置不会生效。</summary>
    Warning
}

public sealed record FlashFlowIssue(FlashFlowIssueKind Kind, int StepId, string Message)
{
    public override string ToString() => StepId > 0 ? $"步骤 {StepId}：{Message}" : Message;
}

/// <summary>
/// 根据 <see cref="FlashFlowExecutor"/> 当前具备的执行能力校验 BOOT 刷写流程。
/// </summary>
/// <remarks>
/// 校验结果分为以下两类：
/// <list type="bullet">
/// <item>
/// <see cref="FlashFlowIssueKind.Error"/> 表示流程将执行失败，或者关键操作会被静默跳过。
/// 此时必须拒绝刷写，避免在流程未被完整执行时仍报告刷写成功。
/// </item>
/// <item>
/// <see cref="FlashFlowIssueKind.Warning"/> 表示流程仍可执行，但部分非关键配置不会生效，
/// 例如响应匹配、校验规则或节点级 Tester Present 周期。必须向操作人员明确提示这些限制。
/// </item>
/// </list>
/// </remarks>
public static class FlashFlowValidator
{
    private const byte SecurityAccessService = 0x27;

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
            if (!FlashStepTypes.TryParse(step.StepType, out var kind))
            {
                issues.Add(new FlashFlowIssue(
                    FlashFlowIssueKind.Error,
                    step.Id,
                    $"未知的步骤类型 \"{step.StepType}\"，只能是 {FlashStepTypes.DownloadDriver} 或 {FlashStepTypes.DownloadApplication}。"));
                continue;
            }

            if (kind == FlashStepKind.Uds && !HexUtil.TryParseByte(step.Service, out _))
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
    /// 校验 0x27 安全访问中的请求 seed 与发送 key 步骤。
    /// ISO 14229 使用奇数子功能请求 seed，使用偶数子功能发送 key，
    /// 因此发送 key 之前必须已经出现请求 seed 的步骤。
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
    /// 报告流程中已声明但 <see cref="FlashFlowExecutor"/> 当前尚未使用的配置。
    /// 未执行的 CRC 校验和擦除例程会产生错误并阻止刷写；响应匹配、校验规则及
    /// 节点级 Tester Present 周期会产生警告，以免操作人员误以为这些配置已经生效。
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
        return FlashStepTypes.IsDownload(step.StepType);
    }
}
