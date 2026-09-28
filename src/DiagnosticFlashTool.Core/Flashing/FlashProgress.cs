namespace DiagnosticFlashTool.Core.Flashing;

public sealed record FlashProgress(int Percent, string Message);

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
