namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>按照 BOOT 配置执行一次 ECU 刷写流程。</summary>
public interface IFlashFlowExecutor
{
    /// <summary>
    /// 严格按照 <see cref="FlashExecutionRequest.BootConfig"/> 中的步骤顺序执行刷写流程。
    /// 每个步骤必须等待上一步成功完成后才能开始，步骤之间不得并行执行。
    /// 用户取消时抛出 <see cref="OperationCanceledException"/>；
    /// 可预期的校验、配置和通信故障通过失败的 <see cref="FlashResult"/> 返回。
    /// </summary>
    /// <param name="request">
    /// 本次刷写的完整输入，包含 BOOT 流程、固件集合以及可选的 UDS 时序配置。
    /// </param>
    /// <param name="progress">
    /// 可选的进度接收器；用于接收整体完成百分比和当前步骤说明，传入 <see langword="null"/> 时不报告进度。
    /// </param>
    /// <param name="cancellationToken">
    /// 用于取消当前刷写；取消后执行器会尽力结束已开始的传输并将 ECU 恢复到默认会话。
    /// </param>
    /// <returns>
    /// 表示异步执行的任务。成功或可预期的刷写失败通过 <see cref="FlashResult"/> 返回。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="OperationCanceledException">请求取消刷写。</exception>
    Task<FlashResult> ExecuteAsync(
        FlashExecutionRequest request,
        IProgress<FlashProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
