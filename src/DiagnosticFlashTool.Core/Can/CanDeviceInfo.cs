namespace DiagnosticFlashTool.Core.Can;

public sealed record CanDeviceInfo(
    string Model,
    string SerialNumber,
    uint ChannelCount,
    bool SupportsCanFd,
    uint DeviceType = 0,
    uint DeviceIndex = 0);
