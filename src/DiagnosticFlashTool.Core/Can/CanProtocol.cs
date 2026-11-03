namespace DiagnosticFlashTool.Core.Can;

public enum CanProtocol
{
    Can,
    CanFd
}

public static class CanProtocolExtensions
{
    public static CanProtocol Parse(string? value)
    {
        return string.Equals(value?.Trim(), "CANFD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "CAN FD", StringComparison.OrdinalIgnoreCase)
            ? CanProtocol.CanFd
            : CanProtocol.Can;
    }

    public static string ToConfigValue(this CanProtocol protocol) =>
        protocol == CanProtocol.CanFd ? "CANFD" : "CAN";
}
