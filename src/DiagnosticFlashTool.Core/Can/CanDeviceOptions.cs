namespace DiagnosticFlashTool.Core.Can;

public sealed class CanDeviceOptions
{
    public string DeviceType { get; set; } = "Mock";
    public uint DeviceIndex { get; set; }
    public uint Channel { get; set; }
    public uint BaudRate { get; set; } = 500_000;
    public bool AutoFlashEnabled { get; set; }
}
