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
}
