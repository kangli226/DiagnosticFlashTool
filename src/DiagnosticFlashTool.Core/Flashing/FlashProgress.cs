namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>
/// 刷写进度。StepId 为当前正在执行的流程节点号，供界面高亮对应行；
/// 开始、完成等非步骤级进度为 null。
/// </summary>
public sealed record FlashProgress(int Percent, string Message, int? StepId = null);

public enum FlashFailureKind
{
    Validation,
    Configuration,
    Timeout,
    Protocol,
    Transport,
    SecurityAccess
}

public sealed class FlashResult
{
    public bool Success { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public FlashFailureKind? FailureKind { get; init; }
    public IReadOnlyList<string> LogMessages { get; init; } = [];
}
