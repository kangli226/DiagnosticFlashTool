namespace DiagnosticFlashTool.Core.Can;

public sealed record CanFrame(
    uint Id,
    byte[] Data,
    uint Channel = 0,
    bool IsExtended = true,
    bool IsRemote = false,
    DateTimeOffset? Timestamp = null)
{
    public int Dlc => Data.Length;

    public override string ToString()
    {
        var bytes = string.Join(" ", Data.Select(b => b.ToString("X2")));
        return $"CH{Channel} 0x{Id:X8} [{Dlc}] {bytes}";
    }
}
