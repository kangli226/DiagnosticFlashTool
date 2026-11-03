namespace DiagnosticFlashTool.Core.Can;

public sealed class CanDeviceOptions
{
    public string DeviceType { get; set; } = "Mock";
    public uint DeviceIndex { get; set; }
    public uint Channel { get; set; }
    public uint BaudRate { get; set; } = 500_000;
    public CanProtocol Protocol { get; set; } = CanProtocol.Can;
    public uint CanFdDataBaudRate { get; set; } = 2_000_000;
    public bool UseBitRateSwitch { get; set; } = true;
}
