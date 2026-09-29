using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>校验通用主程序的流程只能由受支持的内置节点模板组成。</summary>
public static class BuiltInFlowTemplateValidator
{
    public static IReadOnlyList<FlashFlowIssue> Validate(BootConfig bootConfig)
    {
        ArgumentNullException.ThrowIfNull(bootConfig);

        var issues = new List<FlashFlowIssue>();
        foreach (var step in bootConfig.Flow)
        {
            ValidateStep(step, issues);
        }

        return issues;
    }

    private static void ValidateStep(FlashStepConfig step, List<FlashFlowIssue> issues)
    {
        var template = ResolveDeclaredTemplate(step, issues);
        if (template is not null
            && !BuiltInFlowStepTemplateCatalog.MatchesFixedFields(step, template))
        {
            issues.Add(Error(
                step,
                $"节点内容与模板“{template.Title}”不一致，请使用“更换模板”恢复模板固定字段。"));
        }

        if (!string.IsNullOrWhiteSpace(step.AddressingMode)
            && !string.Equals(step.AddressingMode.Trim(), "physical", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Error(step, "内置节点仅允许物理寻址（physical）。"));
        }

        if (!string.IsNullOrWhiteSpace(step.CrcAlgorithm))
        {
            issues.Add(Error(
                step,
                $"crcAlgorithm=“{step.CrcAlgorithm}”尚未实现，请通过“更换模板”清除该配置。"));
        }

        if (template?.AlgorithmUsage != FlowStepTemplateAlgorithmUsage.Security)
        {
            if (!string.IsNullOrWhiteSpace(step.SecurityAlgorithm))
            {
                issues.Add(Error(step, "只有发送 Key 节点允许配置 securityAlgorithm。"));
            }

            if (step.AlgorithmParams.Count > 0)
            {
                issues.Add(Error(step, "只有发送 Key 节点允许配置 algorithmParams。"));
            }
        }
    }

    private static FlowStepTemplateDefinition? ResolveDeclaredTemplate(
        FlashStepConfig step,
        List<FlashFlowIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(step.TemplateId))
        {
            var declared = BuiltInFlowStepTemplateCatalog.FindById(step.TemplateId);
            if (declared is null)
            {
                issues.Add(Error(step, $"未知的节点模板 ID“{step.TemplateId}”，请更换为内置模板。"));
            }

            return declared;
        }

        var inferred = BuiltInFlowStepTemplateCatalog.FindMatchingTemplate(step);
        if (inferred is null)
        {
            issues.Add(Error(step, "节点不属于任何内置模板，当前不允许自定义 UDS 诊断协议。"));
        }

        return inferred;
    }

    private static FlashFlowIssue Error(FlashStepConfig step, string message) =>
        new(FlashFlowIssueKind.Error, step.Id, message);
}
