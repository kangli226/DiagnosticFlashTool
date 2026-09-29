using DiagnosticFlashTool.Core.Flashing;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed record FlowStepTemplate(FlowStepTemplateDefinition Definition)
{
    public string Id => Definition.Id;
    public string Category => Definition.Category;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string DisplayText => $"{Category} · {Title}（{Description}）";

    public override string ToString() => DisplayText;

    public FlowStepEditorRow CreateRow(int id)
    {
        return FlowStepEditorRow.FromConfig(Definition.CreateStep(id));
    }

    public static IReadOnlyList<FlowStepTemplate> BuiltIn { get; } =
        BuiltInFlowStepTemplateCatalog.Templates.Select(definition => new FlowStepTemplate(definition)).ToList();
}
