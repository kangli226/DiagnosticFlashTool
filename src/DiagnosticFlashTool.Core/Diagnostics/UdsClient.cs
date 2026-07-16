namespace DiagnosticFlashTool.Core.Diagnostics;

public sealed class UdsClient
{
    private readonly IsoTpTransport _transport;

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
        var request = new[] { serviceId }.Concat(parameters).ToArray();
        var response = await _transport.SendAndReceiveAsync(request, addressing, responseTimeout, cancellationToken).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow + pendingTimeout;
        while (IsResponsePending(response) && DateTimeOffset.UtcNow < deadline)
        {
            response = await _transport.WaitForPayloadAsync(responseTimeout, cancellationToken).ConfigureAwait(false);
        }

        return UdsResponse.FromPayload(request, response);
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

    private static bool IsResponsePending(byte[] payload)
    {
        return payload is [0x7F, _, 0x78, ..];
    }
}
