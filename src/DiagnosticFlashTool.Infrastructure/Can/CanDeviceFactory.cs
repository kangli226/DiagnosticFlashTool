using DiagnosticFlashTool.Core.Can;

namespace DiagnosticFlashTool.Infrastructure.Can;

public sealed class CanDeviceFactory
{
    public ICanDevice Create(string deviceType)
    {
        return string.Equals(deviceType, "Mock", StringComparison.OrdinalIgnoreCase)
            ? new MockCanDevice()
            : new ZlgCanDevice();
    }

    public ICanDeviceProbe CreateProbe(string deviceType)
    {
        return ZlgDeviceCatalog.IsZlg(deviceType)
            ? new ZlgCanDeviceProbe()
            : new MockCanDeviceProbe();
    }

    private sealed class MockCanDeviceProbe : ICanDeviceProbe
    {
        public Task<CanDeviceInfo?> ProbeAsync(string deviceType, uint deviceIndex = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<CanDeviceInfo?>(new("Mock CAN/CANFD", "MOCK-0001", 2, true, 0, deviceIndex));
        }
    }
}
