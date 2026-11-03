using DiagnosticFlashTool.Core.Can;

namespace DiagnosticFlashTool.Core.Diagnostics;

public sealed class IsoTpTransport : IDisposable
{
    private readonly ICanDevice _device;
    private readonly DiagnosticTransportOptions _options;
    private readonly SemaphoreSlim _transactionGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Queue<byte[]> _pendingPayloads = new();
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

    /// <summary>
    /// Sends an ISO-TP payload without waiting for a diagnostic response.
    /// The same transaction gate used by request/response operations prevents
    /// Tester Present traffic from interleaving with a flash request.
    /// </summary>
    public async Task SendOnlyAsync(
        byte[] payload,
        UdsAddressing addressing,
        CancellationToken cancellationToken)
    {
        await _transactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendPayloadAsync(payload, addressing, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public async Task<byte[]> WaitForPayloadAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource<byte[]> waiter;
        Task<byte[]> task;
        lock (_sync)
        {
            // 发送期间可能已经同步收到响应。必须先消费现有等待器，才能读取
            // 后续排队的响应，否则会丢失首个响应或颠倒 0x78 与最终响应的顺序。
            if (_receiveWaiter is null)
            {
                if (_pendingPayloads.Count > 0)
                {
                    return _pendingPayloads.Dequeue();
                }

                _receiveWaiter = NewWaiter();
            }

            waiter = _receiveWaiter;
            task = waiter.Task;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(timeout, timeoutCts.Token);
        var completed = await Task.WhenAny(task, delayTask).ConfigureAwait(false);
        if (completed == task || task.IsCompleted)
        {
            await timeoutCts.CancelAsync().ConfigureAwait(false);
            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                ClearReceiveWaiter(waiter);
            }
        }

        ClearReceiveWaiter(waiter);
        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"等待 UDS 响应超过 {timeout.TotalMilliseconds:0} 毫秒。");
    }

    private async Task SendPayloadAsync(byte[] payload, UdsAddressing addressing, CancellationToken cancellationToken)
    {
        if (payload.Length > 4095)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "ISO-TP 数据长度不能超过 4095 字节。");
        }

        var requestId = addressing == UdsAddressing.Functional ? _options.FunctionalRequestId : _options.PhysicalRequestId;
        var frameLength = GetFrameDataLength();
        var singleFramePayloadLength = frameLength > 8 ? frameLength - 2 : 7;

        if (payload.Length <= singleFramePayloadLength)
        {
            var data = new byte[frameLength];
            var payloadOffset = 1;
            if (frameLength > 8)
            {
                data[0] = 0;
                data[1] = (byte)payload.Length;
                payloadOffset = 2;
            }
            else
            {
                data[0] = (byte)payload.Length;
            }

            Buffer.BlockCopy(payload, 0, data, payloadOffset, payload.Length);
            await SendFrameAsync(requestId, data, cancellationToken).ConfigureAwait(false);
            return;
        }

        var firstFrame = new byte[frameLength];
        firstFrame[0] = (byte)(0x10 | ((payload.Length >> 8) & 0x0F));
        firstFrame[1] = (byte)(payload.Length & 0xFF);
        var firstPayloadLength = Math.Min(frameLength - 2, payload.Length);
        Buffer.BlockCopy(payload, 0, firstFrame, 2, firstPayloadLength);
        await SendFrameAsync(requestId, firstFrame, cancellationToken).ConfigureAwait(false);

        await WaitForFlowControlAsync(TimeSpan.FromMilliseconds(1000), cancellationToken).ConfigureAwait(false);

        var offset = firstPayloadLength;
        byte sequence = 1;
        while (offset < payload.Length)
        {
            var frame = new byte[frameLength];
            frame[0] = (byte)(0x20 | (sequence & 0x0F));
            var count = Math.Min(frameLength - 1, payload.Length - offset);
            Buffer.BlockCopy(payload, offset, frame, 1, count);
            await SendFrameAsync(requestId, frame, cancellationToken).ConfigureAwait(false);
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

        throw new TimeoutException("等待 ISO-TP 流控帧超时。");
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
        var offset = frame.Data.Length > 8 && frame.Data[0] == 0 ? 2 : 1;
        var declaredLength = offset == 2 ? frame.Data[1] : frame.Data[0] & 0x0F;
        var length = Math.Min(declaredLength, Math.Max(0, frame.Data.Length - offset));
        var payload = frame.Data.Skip(offset).Take(length).ToArray();
        CompletePayload(payload);
    }

    private void StartMultiFrame(CanFrame frame)
    {
        lock (_sync)
        {
            _expectedLength = ((frame.Data[0] & 0x0F) << 8) | frame.Data[1];
            _rxBuffer = frame.Data.Skip(2).Take(Math.Min(frame.Data.Length - 2, _expectedLength)).ToList();
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
                _receiveWaiter?.TrySetException(new InvalidOperationException($"ISO-TP 连续帧序号为 {sequence}，预期为 {_nextConsecutiveFrame}。"));
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
        var data = new byte[GetFrameDataLength()];
        data[0] = 0x30;
        data[1] = (byte)_options.FlowControlBlockSize;
        data[2] = (byte)_options.FlowControlStMinMs;
        await SendFrameAsync(_options.PhysicalRequestId, data, CancellationToken.None).ConfigureAwait(false);
    }

    private int GetFrameDataLength() => Math.Clamp(_options.FrameDataLength, 8, 64);

    private Task SendFrameAsync(uint id, byte[] data, CancellationToken cancellationToken)
    {
        return _device.SendAsync(
            new CanFrame(
                id,
                data,
                _options.Channel,
                _options.ExtendedFrame,
                IsCanFd: GetFrameDataLength() > 8,
                BitRateSwitch: GetFrameDataLength() > 8),
            cancellationToken);
    }

    private void PrepareReceiveWaiter()
    {
        lock (_sync)
        {
            _receiveWaiter = NewWaiter();
            _pendingPayloads.Clear();
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
        }
    }

    private void ClearReceiveWaiter(TaskCompletionSource<byte[]> waiter)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_receiveWaiter, waiter))
            {
                _receiveWaiter = null;
            }
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
        if (_receiveWaiter is { Task.IsCompleted: false } waiter)
        {
            waiter.TrySetResult(payload);
        }
        else
        {
            _pendingPayloads.Enqueue(payload);
        }
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
