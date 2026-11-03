using DiagnosticFlashTool.Core.Can;

namespace DiagnosticFlashTool.Infrastructure.Can;

internal sealed record ZlgDeviceDescriptor(
    string Model,
    uint DeviceType,
    uint DefaultChannelCount,
    bool SupportsCanFd);

internal static class ZlgDeviceCatalog
{
    public const string GenericDeviceType = "ZLGCAN";
    public const string LegacyDeviceType = "ZLG USBCAN-2A (4)";

    private static readonly ZlgDeviceDescriptor[] Devices =
    [
        new("USBCAN-2A", 4, 2, false),
        new("USBCAN-E-U", 20, 2, false),
        new("USBCAN-2E-U", 21, 2, false),
        new("USBCAN-4E-U", 31, 4, false),
        new("USBCAN-8E-U", 34, 8, false),
        new("PCIECANFD-100U", 38, 1, true),
        new("PCIECANFD-200U", 39, 2, true),
        new("PCIECANFD-400U", 40, 4, true),
        new("USBCANFD-200U", 41, 2, true),
        new("USBCANFD-100U", 42, 1, true),
        new("USBCANFD-MINI", 43, 1, true),
        new("USBCANFD-800U", 59, 8, true),
        new("USBCANFD-400U", 76, 4, true),
        new("PCIECANFD-800U", 82, 8, true)
    ];

    public static IReadOnlyList<ZlgDeviceDescriptor> ResolveCandidates(string? configuredType)
    {
        var normalized = configuredType?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, GenericDeviceType, StringComparison.OrdinalIgnoreCase))
        {
            return Devices;
        }

        if (string.Equals(normalized, LegacyDeviceType, StringComparison.OrdinalIgnoreCase))
        {
            return [Devices[0]];
        }

        if (uint.TryParse(normalized, out var numericType))
        {
            var numeric = Devices.FirstOrDefault(item => item.DeviceType == numericType);
            return numeric is null
                ? [new ZlgDeviceDescriptor($"ZLG ({numericType})", numericType, 1, false)]
                : [numeric];
        }

        var descriptor = Devices.FirstOrDefault(item =>
            string.Equals(item.Model, normalized, StringComparison.OrdinalIgnoreCase));
        return descriptor is null ? [] : [descriptor];
    }

    public static bool IsZlg(string? deviceType) =>
        !string.Equals(deviceType?.Trim(), "Mock", StringComparison.OrdinalIgnoreCase);

    public static CanDeviceInfo CreateInfo(
        ZlgDeviceDescriptor descriptor,
        uint deviceIndex,
        ZlgNative.DeviceInfo nativeInfo)
    {
        var model = ZlgNative.ReadFixedString(nativeInfo.HardwareType);
        var serial = ZlgNative.ReadFixedString(nativeInfo.SerialNumber);
        var channelCount = nativeInfo.ChannelCount > 0
            ? Math.Clamp((uint)nativeInfo.ChannelCount, 1, 16)
            : descriptor.DefaultChannelCount;
        var supportsCanFd = descriptor.SupportsCanFd
            || model.Contains("CANFD", StringComparison.OrdinalIgnoreCase);
        return new CanDeviceInfo(
            string.IsNullOrWhiteSpace(model) ? descriptor.Model : model,
            serial,
            channelCount,
            supportsCanFd,
            descriptor.DeviceType,
            deviceIndex);
    }
}
