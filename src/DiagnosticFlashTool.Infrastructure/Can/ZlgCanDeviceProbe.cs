using DiagnosticFlashTool.Core.Can;

namespace DiagnosticFlashTool.Infrastructure.Can;

public sealed class ZlgCanDeviceProbe : ICanDeviceProbe
{
    public Task<CanDeviceInfo?> ProbeAsync(string deviceType, uint deviceIndex = 0, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Probe(deviceType, deviceIndex, cancellationToken), cancellationToken);
    }

    private static CanDeviceInfo? Probe(string deviceType, uint preferredIndex, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        ZlgNative.ConfigureSearchPath();
        foreach (var descriptor in ZlgDeviceCatalog.ResolveCandidates(deviceType))
        {
            foreach (var index in CandidateIndices(preferredIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var handle = ZlgNative.ZCAN_OpenDevice(descriptor.DeviceType, index, 0);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var nativeInfo = CreateDeviceInfo();
                    if (ZlgNative.ZCAN_GetDeviceInf(handle, ref nativeInfo) != ZlgNative.StatusOk)
                    {
                        nativeInfo.ChannelCount = (byte)descriptor.DefaultChannelCount;
                    }

                    return ZlgDeviceCatalog.CreateInfo(descriptor, index, nativeInfo);
                }
                finally
                {
                    ZlgNative.ZCAN_CloseDevice(handle);
                }
            }
        }

        return null;
    }

    private static IEnumerable<uint> CandidateIndices(uint preferred)
    {
        yield return preferred;
        for (uint index = 0; index < 8; index++)
        {
            if (index != preferred)
            {
                yield return index;
            }
        }
    }

    internal static ZlgNative.DeviceInfo CreateDeviceInfo() => new()
    {
        SerialNumber = new byte[20],
        HardwareType = new byte[40],
        Reserved = new ushort[4]
    };
}
