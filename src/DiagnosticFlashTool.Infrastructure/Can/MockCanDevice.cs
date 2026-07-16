using DiagnosticFlashTool.Core.Can;

namespace DiagnosticFlashTool.Infrastructure.Can;

public sealed class MockCanDevice : ICanDevice
{
    private readonly object _sync = new();
    private bool _isOpen;
    private uint _responseId = 0x18DA3555;
    private uint _channel;
    private int _expectedLength;
    private List<byte>? _rxBuffer;
    private byte _nextConsecutiveFrame = 1;

    public event EventHandler<CanFrame>? FrameReceived;
    public event EventHandler<CanFrame>? FrameSent;

    public bool IsOpen => _isOpen;

    public Task OpenAsync(CanDeviceOptions options, CancellationToken cancellationToken)
    {
        _isOpen = true;
        _channel = options.Channel;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        _isOpen = false;
        return Task.CompletedTask;
    }

    public Task SendAsync(CanFrame frame, CancellationToken cancellationToken)
    {
        if (!_isOpen)
        {
            throw new InvalidOperationException("Mock CAN device is not open.");
        }

        FrameSent?.Invoke(this, frame);
        HandleIsoTpFromClient(frame, cancellationToken);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _isOpen = false;
        return ValueTask.CompletedTask;
    }

    private void HandleIsoTpFromClient(CanFrame frame, CancellationToken cancellationToken)
    {
        if (frame.Data.Length == 0)
        {
            return;
        }

        var pciType = frame.Data[0] >> 4;
        switch (pciType)
        {
            case 0:
                var length = frame.Data[0] & 0x0F;
                var payload = frame.Data.Skip(1).Take(length).ToArray();
                _ = RespondToRequestAsync(payload, cancellationToken);
                break;
            case 1:
                lock (_sync)
                {
                    _expectedLength = ((frame.Data[0] & 0x0F) << 8) | frame.Data[1];
                    _rxBuffer = frame.Data.Skip(2).Take(Math.Min(6, _expectedLength)).ToList();
                    _nextConsecutiveFrame = 1;
                }

                EmitFrame([0x30, 0x00, 0x00, 0, 0, 0, 0, 0]);
                break;
            case 2:
                byte[]? completed = null;
                lock (_sync)
                {
                    if (_rxBuffer is null || _expectedLength <= 0)
                    {
                        return;
                    }

                    var sequence = (byte)(frame.Data[0] & 0x0F);
                    if (sequence != _nextConsecutiveFrame)
                    {
                        return;
                    }

                    _nextConsecutiveFrame = (byte)((_nextConsecutiveFrame + 1) & 0x0F);
                    _rxBuffer.AddRange(frame.Data.Skip(1));

                    if (_rxBuffer.Count >= _expectedLength)
                    {
                        completed = _rxBuffer.Take(_expectedLength).ToArray();
                        _rxBuffer = null;
                        _expectedLength = 0;
                    }
                }

                if (completed is not null)
                {
                    _ = RespondToRequestAsync(completed, cancellationToken);
                }
                break;
        }
    }

    private async Task RespondToRequestAsync(byte[] request, CancellationToken cancellationToken)
    {
        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
        if (request.Length == 0)
        {
            return;
        }

        var service = request[0];
        var response = service switch
        {
            0x10 or 0x11 or 0x28 or 0x31 or 0x85 => PositiveWithSubFunction(request),
            0x27 => SecurityResponse(request),
            0x34 => [0x74, 0x20, 0x0F, 0x00],
            0x36 => [0x76, request.Length > 1 ? request[1] : (byte)0],
            0x37 => [0x77],
            0x2E => PositiveWithSubFunction(request),
            _ => new byte[] { 0x7F, service, 0x11 }
        };

        await EmitPayloadAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] PositiveWithSubFunction(byte[] request)
    {
        return request.Length > 1
            ? [(byte)(request[0] + 0x40), request[1]]
            : [(byte)(request[0] + 0x40)];
    }

    private static byte[] SecurityResponse(byte[] request)
    {
        if (request.Length > 1 && request[1] % 2 == 1)
        {
            return [0x67, request[1], 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];
        }

        return request.Length > 1 ? [0x67, request[1]] : [0x67];
    }

    private async Task EmitPayloadAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length <= 7)
        {
            var data = new byte[8];
            data[0] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, data, 1, payload.Length);
            EmitFrame(data);
            return;
        }

        var first = new byte[8];
        first[0] = (byte)(0x10 | ((payload.Length >> 8) & 0x0F));
        first[1] = (byte)(payload.Length & 0xFF);
        Buffer.BlockCopy(payload, 0, first, 2, 6);
        EmitFrame(first);

        await Task.Delay(20, cancellationToken).ConfigureAwait(false);

        var offset = 6;
        byte sequence = 1;
        while (offset < payload.Length)
        {
            var cf = new byte[8];
            cf[0] = (byte)(0x20 | (sequence & 0x0F));
            var count = Math.Min(7, payload.Length - offset);
            Buffer.BlockCopy(payload, offset, cf, 1, count);
            EmitFrame(cf);
            offset += count;
            sequence = (byte)((sequence + 1) & 0x0F);
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    private void EmitFrame(byte[] data)
    {
        FrameReceived?.Invoke(this, new CanFrame(_responseId, data, _channel));
    }
}
