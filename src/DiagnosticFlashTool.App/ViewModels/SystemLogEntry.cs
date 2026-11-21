namespace DiagnosticFlashTool.App.ViewModels;

public sealed record SystemLogEntry(DateTime Timestamp, DiagnosticStatusKind Kind, string Message)
{
    public int? ProjectNo { get; init; }
    public string? ProjectName { get; init; }

    public string LevelText => Kind switch
    {
        DiagnosticStatusKind.Success => "成功",
        DiagnosticStatusKind.Running => "运行",
        DiagnosticStatusKind.Warning => "警告",
        DiagnosticStatusKind.Error => "错误",
        _ => "普通"
    };

    public string Text => $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{LevelText}] [{(ProjectNo is null ? "通用 UDS" : $"项目：{ProjectName}")}] {Message}";
}
