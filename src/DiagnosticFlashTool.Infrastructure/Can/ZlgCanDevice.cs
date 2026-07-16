using System.Runtime.InteropServices;
using DiagnosticFlashTool.Core.Can;

namespace DiagnosticFlashTool.Infrastructure.Can;

public sealed class ZlgCanDevice : ICanDevice
{
    private const uint Reserved = 0;
    private static bool s_nativeSearchPathConfigured;
    private readonly CancellationTokenSource _receiveLoopCts = new();
    private Task? _receiveLoop;
    private CanDeviceOptions _options = new();

    public event EventHandler<CanFrame>? FrameReceived;
    public event EventHandler<CanFrame>? FrameSent;

    public bool IsOpen { get; private set; }

    public Task OpenAsync(CanDeviceOptions options, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ZLG ControlCAN is only supported on Windows.");
        }

        _options = options;
        ConfigureNativeSearchPath();
        var deviceType = ParseDeviceType(options.DeviceType);
        if (VCI_OpenDevice(deviceType, options.DeviceIndex, Reserved) != 1)
        {
            throw new InvalidOperationException("VCI_OpenDevice failed.");
        }

        var init = BuildInitConfig(options.BaudRate);
        if (VCI_InitCAN(deviceType, options.DeviceIndex, options.Channel, ref init) != 1)
        {
            VCI_CloseDevice(deviceType, options.DeviceIndex);
            throw new InvalidOperationException("VCI_InitCAN failed.");
        }

        if (VCI_StartCAN(deviceType, options.DeviceIndex, options.Channel) != 1)
        {
            VCI_CloseDevice(deviceType, options.DeviceIndex);
            throw new InvalidOperationException("VCI_StartCAN failed.");
        }

        IsOpen = true;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveLoopCts.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            return Task.CompletedTask;
        }

        _receiveLoopCts.Cancel();
        var deviceType = ParseDeviceType(_options.DeviceType);
        VCI_CloseDevice(deviceType, _options.DeviceIndex);
        IsOpen = false;
        return Task.CompletedTask;
    }

    public Task SendAsync(CanFrame frame, CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("ZLG CAN device is not open.");
        }

        var obj = new VciCanObj
        {
            Id = frame.Id,
            SendType = 0,
            RemoteFlag = frame.IsRemote ? (byte)1 : (byte)0,
            ExternFlag = frame.IsExtended ? (byte)1 : (byte)0,
            DataLen = (byte)Math.Min(8, frame.Data.Length),
            Data = new byte[8],
            Reserved = new byte[3]
        };
        Array.Copy(frame.Data, obj.Data, obj.DataLen);

        var result = VCI_Transmit(ParseDeviceType(_options.DeviceType), _options.DeviceIndex, frame.Channel, [obj], 1);
        if (result != 1)
        {
            throw new InvalidOperationException("VCI_Transmit failed.");
        }

        FrameSent?.Invoke(this, frame);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        _receiveLoopCts.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var deviceType = ParseDeviceType(_options.DeviceType);
        while (!cancellationToken.IsCancellationRequested)
        {
            var buffer = new VciCanObj[100];
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i].Data = new byte[8];
                buffer[i].Reserved = new byte[3];
            }

            var count = VCI_Receive(deviceType, _options.DeviceIndex, _options.Channel, buffer, (uint)buffer.Length, 10);
            for (var i = 0; i < count; i++)
            {
                var item = buffer[i];
                var data = item.Data.Take(item.DataLen).ToArray();
                FrameReceived?.Invoke(this, new CanFrame(item.Id, data, _options.Channel, item.ExternFlag != 0, item.RemoteFlag != 0));
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }

    private static uint ParseDeviceType(string? deviceType)
    {
        if (uint.TryParse(deviceType, out var value))
        {
            return value;
        }

        return 4;
    }

    private static void ConfigureNativeSearchPath()
    {
        if (s_nativeSearchPathConfigured)
        {
            return;
        }

        var nativeDirectory = Path.Combine(AppContext.BaseDirectory, "resources", "native");
        if (Directory.Exists(nativeDirectory) && !SetDllDirectory(nativeDirectory))
        {
            throw new InvalidOperationException($"Failed to set native DLL directory: {nativeDirectory}");
        }

        s_nativeSearchPathConfigured = true;
    }

    private static VciInitConfig BuildInitConfig(uint baudRate)
    {
        var (timing0, timing1) = baudRate switch
        {
            250_000 => ((byte)0x01, (byte)0x1C),
            500_000 => ((byte)0x00, (byte)0x1C),
            1_000_000 => ((byte)0x00, (byte)0x14),
            _ => ((byte)0x00, (byte)0x1C)
        };

        return new VciInitConfig
        {
            AccCode = 0,
            AccMask = 0xFFFFFFFF,
            Reserved = 0,
            Filter = 1,
            Timing0 = timing0,
            Timing1 = timing1,
            Mode = 0
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VciInitConfig
    {
        public uint AccCode;
        public uint AccMask;
        public uint Reserved;
        public byte Filter;
        public byte Timing0;
        public byte Timing1;
        public byte Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VciCanObj
    {
        public uint Id;
        public uint TimeStamp;
        public byte TimeFlag;
        public byte SendType;
        public byte RemoteFlag;
        public byte ExternFlag;
        public byte DataLen;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Data;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] Reserved;
    }

    [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint VCI_OpenDevice(uint deviceType, uint deviceIndex, uint reserved);

    [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint VCI_CloseDevice(uint deviceType, uint deviceIndex);

    [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint VCI_InitCAN(uint deviceType, uint deviceIndex, uint canIndex, ref VciInitConfig initConfig);

    [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint VCI_StartCAN(uint deviceType, uint deviceIndex, uint canIndex);

    [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint VCI_Transmit(uint deviceType, uint deviceIndex, uint canIndex, [In] VciCanObj[] send, uint len);

    [DllImport("ControlCAN.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint VCI_Receive(uint deviceType, uint deviceIndex, uint canIndex, [Out] VciCanObj[] receive, uint len, int waitTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
}
