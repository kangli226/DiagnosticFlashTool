using DiagnosticFlashTool.Core.Can;

namespace DiagnosticFlashTool.Core.Diagnostics;

public sealed class IsoTpTransport : IDisposable
{
    private readonly ICanDevice _device;
    private readonly DiagnosticTransportOptions _options;
    private readonly SemaphoreSlim _transactionGate = new(1, 1);
    private readonly object _sync = new();
    private TaskCompletionSource<byte[]>? _receiveWaiter;
    private int _expectedLength;
    private List<byte>? _rxBuffer;
    private byte _nextConsecutiveFrame = 1;

    public IsoTpTransport(ICanDevice device, DiagnosticTransportOptions options)
    {
        _device = device;
        _options = options;
        _device.FrameReceived += OnFrameReceived;
    }

    public async Task<byte[]> SendAndReceiveAsync(
        byte[] payload,
        UdsAddressing addressing,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await _transactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PrepareReceiveWaiter();
            await SendPayloadAsync(payload, addressing, cancellationToken).ConfigureAwait(false);
            return await WaitForPayloadAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ClearReceiveWaiter();
            _transactionGate.Release();
        }
    }

    public async Task<byte[]> WaitForPayloadAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task<byte[]> task;
        lock (_sync)
        {
            _receiveWaiter ??= NewWaiter();
            task = _receiveWaiter.Task;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(timeout, timeoutCts.Token);
        var completed = await Task.WhenAny(task, delayTask).ConfigureAwait(false);
        if (completed == task)
        {
            await timeoutCts.CancelAsync().ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }

        throw new TimeoutException($"UDS response timeout after {timeout.TotalMilliseconds:0} ms.");
    }

    private async Task SendPayloadAsync(byte[] payload, UdsAddressing addressing, CancellationToken cancellationToken)
    {
        if (payload.Length > 4095)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "ISO-TP classic CAN payload is limited to 4095 bytes.");
        }

        var requestId = addressing == UdsAddressing.Functional ? _options.FunctionalRequestId : _options.PhysicalRequestId;

        if (payload.Length <= 7)
        {
            var data = new byte[8];
            data[0] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, data, 1, payload.Length);
            await _device.SendAsync(new CanFrame(requestId, data, _options.Channel, _options.ExtendedFrame), cancellationToken).ConfigureAwait(false);
            return;
        }

        var firstFrame = new byte[8];
        firstFrame[0] = (byte)(0x10 | ((payload.Length >> 8) & 0x0F));
        firstFrame[1] = (byte)(payload.Length & 0xFF);
        Buffer.BlockCopy(payload, 0, firstFrame, 2, 6);
        await _device.SendAsync(new CanFrame(requestId, firstFrame, _options.Channel, _options.ExtendedFrame), cancellationToken).ConfigureAwait(false);

        await WaitForFlowControlAsync(TimeSpan.FromMilliseconds(1000), cancellationToken).ConfigureAwait(false);

        var offset = 6;
        byte sequence = 1;
        while (offset < payload.Length)
        {
            var frame = new byte[8];
            frame[0] = (byte)(0x20 | (sequence & 0x0F));
            var count = Math.Min(7, payload.Length - offset);
            Buffer.BlockCopy(payload, offset, frame, 1, count);
            await _device.SendAsync(new CanFrame(requestId, frame, _options.Channel, _options.ExtendedFrame), cancellationToken).ConfigureAwait(false);
            offset += count;
            sequence = (byte)((sequence + 1) & 0x0F);

            if (_options.FlowControlStMinMs > 0)
            {
                await Task.Delay(_options.FlowControlStMinMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WaitForFlowControlAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (_rxBuffer is null && _expectedLength == -1)
                {
                    _expectedLength = 0;
                    return;
                }
            }
        }

        throw new TimeoutException("Timeout waiting for ISO-TP flow control frame.");
    }

    private void OnFrameReceived(object? sender, CanFrame frame)
    {
        if (frame.Id != _options.ResponseId || frame.Data.Length == 0)
        {
            return;
        }

        var pci = frame.Data[0];
        var type = pci >> 4;

        if (type == 0x3)
        {
            lock (_sync)
            {
                _expectedLength = -1;
            }
            return;
        }

        switch (type)
        {
            case 0x0:
                CompleteSingleFrame(frame);
                break;
            case 0x1:
                StartMultiFrame(frame);
                _ = SendFlowControlAsync();
                break;
            case 0x2:
                ContinueMultiFrame(frame);
                break;
        }
    }

    private void CompleteSingleFrame(CanFrame frame)
    {
        var length = Math.Min(frame.Data[0] & 0x0F, Math.Max(0, frame.Data.Length - 1));
        var payload = frame.Data.Skip(1).Take(length).ToArray();
        CompletePayload(payload);
    }

    private void StartMultiFrame(CanFrame frame)
    {
        lock (_sync)
        {
            _expectedLength = ((frame.Data[0] & 0x0F) << 8) | frame.Data[1];
            _rxBuffer = frame.Data.Skip(2).Take(Math.Min(6, _expectedLength)).ToList();
            _nextConsecutiveFrame = 1;

            if (_rxBuffer.Count >= _expectedLength)
            {
                CompletePayloadLocked(_rxBuffer.Take(_expectedLength).ToArray());
            }
        }
    }

    private void ContinueMultiFrame(CanFrame frame)
    {
        lock (_sync)
        {
            if (_rxBuffer is null || _expectedLength <= 0)
            {
                return;
            }

            var sequence = (byte)(frame.Data[0] & 0x0F);
            if (sequence != _nextConsecutiveFrame)
            {
                _receiveWaiter?.TrySetException(new InvalidOperationException($"Unexpected ISO-TP CF sequence {sequence}, expected {_nextConsecutiveFrame}."));
                return;
            }

            _nextConsecutiveFrame = (byte)((_nextConsecutiveFrame + 1) & 0x0F);
            _rxBuffer.AddRange(frame.Data.Skip(1));

            if (_rxBuffer.Count >= _expectedLength)
            {
                CompletePayloadLocked(_rxBuffer.Take(_expectedLength).ToArray());
            }
        }
    }

    private async Task SendFlowControlAsync()
    {
        var data = new byte[8];
        data[0] = 0x30;
        data[1] = (byte)_options.FlowControlBlockSize;
        data[2] = (byte)_options.FlowControlStMinMs;
        await _device.SendAsync(new CanFrame(_options.PhysicalRequestId, data, _options.Channel, _options.ExtendedFrame), CancellationToken.None).ConfigureAwait(false);
    }

    private void PrepareReceiveWaiter()
    {
        lock (_sync)
        {
            _receiveWaiter = NewWaiter();
            _expectedLength = 0;
            _rxBuffer = null;
            _nextConsecutiveFrame = 1;
        }
    }

    private void ClearReceiveWaiter()
    {
        lock (_sync)
        {
            _receiveWaiter = null;
            _expectedLength = 0;
            _rxBuffer = null;
        }
    }

    private void CompletePayload(byte[] payload)
    {
        lock (_sync)
        {
            CompletePayloadLocked(payload);
        }
    }

    private void CompletePayloadLocked(byte[] payload)
    {
        _rxBuffer = null;
        _expectedLength = 0;
        _receiveWaiter?.TrySetResult(payload);
    }

    private static TaskCompletionSource<byte[]> NewWaiter()
    {
        return new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Dispose()
    {
        _device.FrameReceived -= OnFrameReceived;
        _transactionGate.Dispose();
    }
}
