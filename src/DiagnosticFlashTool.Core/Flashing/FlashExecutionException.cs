namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>刷写执行期间可转换为失败结果的预期故障。</summary>
internal sealed class FlashExecutionException(
    FlashFailureKind failureKind,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public FlashFailureKind FailureKind { get; } = failureKind;
}
