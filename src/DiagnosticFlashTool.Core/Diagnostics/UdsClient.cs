namespace DiagnosticFlashTool.Core.Diagnostics;

public sealed class UdsClient
{
    private readonly IsoTpTransport _transport;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public UdsClient(IsoTpTransport transport)
    {
        _transport = transport;
    }

    public async Task<UdsResponse> SendAsync(
        byte serviceId,
        IEnumerable<byte> parameters,
        UdsAddressing addressing,
        TimeSpan responseTimeout,
        TimeSpan pendingTimeout,
        CancellationToken cancellationToken)
    {
        return await SendAsync(
            serviceId,
            parameters,
            addressing,
            UdsTimingOptions.FromRequestTimeouts(responseTimeout, pendingTimeout),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<UdsResponse> SendAsync(
        byte serviceId,
        IEnumerable<byte> parameters,
        UdsAddressing addressing,
        UdsTimingOptions timing,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            timing.Validate();
            var request = new[] { serviceId }.Concat(parameters).ToArray();
            var response = await _transport.SendAndReceiveAsync(request, addressing, timing.P2ClientTimeout, cancellationToken).ConfigureAwait(false);

            var deadline = timing.PendingOverallTimeout is { } overall
                ? DateTimeOffset.UtcNow + overall
                : DateTimeOffset.MaxValue;
            while (IsResponsePending(response))
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"UDS pending response timeout after {timing.PendingOverallTimeout?.TotalMilliseconds:0} ms.");
                }

                var waitTimeout = remaining < timing.P2StarClientTimeout
                    ? remaining
                    : timing.P2StarClientTimeout;
                response = await _transport.WaitForPayloadAsync(waitTimeout, cancellationToken).ConfigureAwait(false);
            }

            return UdsResponse.FromPayload(request, response);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public Task<UdsResponse> SendRawAsync(
        byte[] request,
        UdsAddressing addressing,
        TimeSpan responseTimeout,
        TimeSpan pendingTimeout,
        CancellationToken cancellationToken)
    {
        if (request.Length == 0)
        {
            throw new ArgumentException("UDS request payload cannot be empty.", nameof(request));
        }

        return SendAsync(request[0], request.Skip(1), addressing, responseTimeout, pendingTimeout, cancellationToken);
    }

    public Task<UdsResponse> SendRawAsync(
        byte[] request,
        UdsAddressing addressing,
        UdsTimingOptions timing,
        CancellationToken cancellationToken)
    {
        if (request.Length == 0)
        {
            throw new ArgumentException("UDS request payload cannot be empty.", nameof(request));
        }

        return SendAsync(request[0], request.Skip(1), addressing, timing, cancellationToken);
    }

    public async Task SendNoResponseAsync(
        byte serviceId,
        IEnumerable<byte> parameters,
        UdsAddressing addressing,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var request = new[] { serviceId }.Concat(parameters).ToArray();
            await _transport.SendOnlyAsync(request, addressing, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static bool IsResponsePending(byte[] payload)
    {
        return payload is [0x7F, _, 0x78, ..];
    }
}
