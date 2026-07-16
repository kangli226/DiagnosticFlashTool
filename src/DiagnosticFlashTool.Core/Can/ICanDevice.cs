namespace DiagnosticFlashTool.Core.Can;

public interface ICanDevice : IAsyncDisposable
{
    event EventHandler<CanFrame>? FrameReceived;
    event EventHandler<CanFrame>? FrameSent;

    bool IsOpen { get; }

    Task OpenAsync(CanDeviceOptions options, CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
    Task SendAsync(CanFrame frame, CancellationToken cancellationToken);
}
