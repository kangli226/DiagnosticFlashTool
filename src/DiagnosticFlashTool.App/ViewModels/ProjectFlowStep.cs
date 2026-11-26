using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Flashing;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed record ProjectFlowStep(string ServiceName, string SubService, string Description)
{
    public static ProjectFlowStep FromConfig(FlashStepConfig step)
    {
        var template = BuiltInFlowStepTemplateCatalog.FindMatchingTemplate(step);
        var service = step.Service?.Trim();
        var serviceName = template?.Category ?? (string.IsNullOrEmpty(service) ? step.StepType : service);
        if (template is not null && !string.IsNullOrEmpty(service))
        {
            serviceName = $"{template.Category}（{service}）";
        }

        return new(
            FormatValue(serviceName),
            FormatValue(step.SubService),
            FormatValue(template?.Title ?? step.Name));
    }

    private static string FormatValue(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
