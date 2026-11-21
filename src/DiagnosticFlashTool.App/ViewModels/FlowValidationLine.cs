namespace DiagnosticFlashTool.App.ViewModels;

public enum FlowValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 校验结果的一行：严重级别 + 文案。界面按级别取图标与语义色，
/// 同时保留图标和文字，符合"状态不能只靠颜色表达"的规范要求。
/// </summary>
public sealed class FlowValidationLine(FlowValidationSeverity severity, string text)
{
    public FlowValidationSeverity Severity { get; } = severity;

    public string Text { get; } = text;

    public bool IsError => Severity == FlowValidationSeverity.Error;

    public bool IsWarning => Severity == FlowValidationSeverity.Warning;

    public string Icon => Severity switch
    {
        FlowValidationSeverity.Error => "\u2715",
        FlowValidationSeverity.Warning => "!",
        _ => "\u2022"
    };
}
