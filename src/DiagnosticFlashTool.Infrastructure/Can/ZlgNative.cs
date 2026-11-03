using System.Runtime.InteropServices;
using System.Text;

namespace DiagnosticFlashTool.Infrastructure.Can;

internal static class ZlgNative
{
    public const uint StatusOk = 1;
    public const uint TypeCan = 0;
    public const uint TypeCanFd = 1;
    public const byte CanFdBrs = 0x01;
    public const byte CanFdEsi = 0x02;
    public const uint CanEffFlag = 0x80000000;
    public const uint CanRtrFlag = 0x40000000;
    public const uint CanSffMask = 0x000007FF;
    public const uint CanEffMask = 0x1FFFFFFF;

    private static bool s_searchPathConfigured;

    public static void ConfigureSearchPath()
    {
        if (s_searchPathConfigured)
        {
            return;
        }

        var nativeDirectory = Path.Combine(AppContext.BaseDirectory, "resources", "native");
        if (Directory.Exists(nativeDirectory) && !SetDllDirectory(nativeDirectory))
        {
            throw new InvalidOperationException($"无法设置 ZLG 原生库目录：{nativeDirectory}");
        }

        s_searchPathConfigured = true;
    }

    public static string ReadFixedString(byte[]? buffer)
    {
        if (buffer is null || buffer.Length == 0)
        {
            return string.Empty;
        }

        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        return Encoding.Default.GetString(buffer, 0, length).Trim();
    }

    public static uint MakeCanId(uint id, bool extended, bool remote)
    {
        var result = id & (extended ? CanEffMask : CanSffMask);
        if (extended)
        {
            result |= CanEffFlag;
        }

        if (remote)
        {
            result |= CanRtrFlag;
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfo
    {
        public ushort HardwareVersion;
        public ushort FirmwareVersion;
        public ushort DriverVersion;
        public ushort InterfaceVersion;
        public ushort IrqNumber;
        public byte ChannelCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)] public byte[] SerialNumber;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)] public byte[] HardwareType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public ushort[] Reserved;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct ChannelInitConfig
    {
        [FieldOffset(0)] public uint CanType;
        [FieldOffset(4)] public uint AccCode;
        [FieldOffset(8)] public uint AccMask;
        [FieldOffset(12)] public uint ReservedOrArbitrationTiming;
        [FieldOffset(16)] public uint DataTiming;
        [FieldOffset(16)] public byte ClassicFilter;
        [FieldOffset(17)] public byte Timing0;
        [FieldOffset(18)] public byte Timing1;
        [FieldOffset(19)] public byte ClassicMode;
        [FieldOffset(20)] public uint Brp;
        [FieldOffset(24)] public byte CanFdFilter;
        [FieldOffset(25)] public byte CanFdMode;
        [FieldOffset(26)] public ushort Pad;
        [FieldOffset(28)] public uint CanFdReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClassicFrame
    {
        public uint CanId;
        public byte Dlc;
        public byte Pad;
        public byte Reserved0;
        public byte Reserved1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CanFdFrame
    {
        public uint CanId;
        public byte Length;
        public byte Flags;
        public byte Reserved0;
        public byte Reserved1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClassicTransmitData
    {
        public ClassicFrame Frame;
        public uint TransmitType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClassicReceiveData
    {
        public ClassicFrame Frame;
        public ulong Timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CanFdTransmitData
    {
        public CanFdFrame Frame;
        public uint TransmitType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CanFdReceiveData
    {
        public CanFdFrame Frame;
        public ulong Timestamp;
    }

    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr ZCAN_OpenDevice(uint deviceType, uint deviceIndex, uint reserved);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_CloseDevice(IntPtr deviceHandle);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_GetDeviceInf(IntPtr deviceHandle, ref DeviceInfo deviceInfo);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr ZCAN_InitCAN(IntPtr deviceHandle, uint channelIndex, ref ChannelInitConfig initConfig);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_StartCAN(IntPtr channelHandle);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_ResetCAN(IntPtr channelHandle);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_ClearBuffer(IntPtr channelHandle);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_Transmit(IntPtr channelHandle, [In] ClassicTransmitData[] frames, uint length);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_Receive(IntPtr channelHandle, [Out] ClassicReceiveData[] frames, uint length, int waitTime);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_TransmitFD(IntPtr channelHandle, [In] CanFdTransmitData[] frames, uint length);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZCAN_ReceiveFD(IntPtr channelHandle, [Out] CanFdReceiveData[] frames, uint length, int waitTime);
    [DllImport("zlgcan.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] public static extern uint ZCAN_SetValue(IntPtr deviceHandle, string path, string value);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetDllDirectory(string lpPathName);
}
