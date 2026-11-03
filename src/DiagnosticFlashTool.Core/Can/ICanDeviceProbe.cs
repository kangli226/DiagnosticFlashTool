namespace DiagnosticFlashTool.Core.Can;

public interface ICanDeviceProbe
{
    Task<CanDeviceInfo?> ProbeAsync(
        string deviceType,
        uint deviceIndex = 0,
        CancellationToken cancellationToken = default);
}
