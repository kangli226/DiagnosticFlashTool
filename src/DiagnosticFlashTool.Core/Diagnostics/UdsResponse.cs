namespace DiagnosticFlashTool.Core.Diagnostics;

public sealed class UdsResponse
{
    private UdsResponse(byte[] request, byte[] payload)
    {
        Request = request;
        Payload = payload;
    }

    public byte[] Request { get; }
    public byte[] Payload { get; }
    public bool IsNegative => Payload.Length >= 3 && Payload[0] == 0x7F;
    public byte? NegativeResponseCode => IsNegative ? Payload[2] : null;
    public byte? PositiveServiceId => !IsNegative && Payload.Length > 0 ? Payload[0] : null;

    public static UdsResponse FromPayload(byte[] request, byte[] payload) => new(request, payload);

    public void EnsurePositive()
    {
        if (IsNegative)
        {
            throw new InvalidOperationException($"UDS negative response: SID=0x{Payload[1]:X2}, NRC=0x{Payload[2]:X2}.");
        }
    }

    public override string ToString()
    {
        return string.Join(" ", Payload.Select(x => x.ToString("X2")));
    }
}
