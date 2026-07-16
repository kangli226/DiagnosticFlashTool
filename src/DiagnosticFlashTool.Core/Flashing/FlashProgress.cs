namespace DiagnosticFlashTool.Core.Flashing;

public sealed record FlashProgress(int Percent, string Message);

public sealed class FlashResult
{
    public bool Success { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public List<string> LogMessages { get; init; } = [];
}
