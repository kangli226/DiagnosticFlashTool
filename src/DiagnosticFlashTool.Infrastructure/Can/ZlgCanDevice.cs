using DiagnosticFlashTool.Core.Can;

namespace DiagnosticFlashTool.Infrastructure.Can;

public sealed class ZlgCanDevice : ICanDevice
{
    private readonly CancellationTokenSource _receiveLoopCts = new();
    private Task? _receiveLoop;
    private IntPtr _deviceHandle;
    private IntPtr _channelHandle;
    private CanDeviceOptions _options = new();

    public event EventHandler<CanFrame>? FrameReceived;
    public event EventHandler<CanFrame>? FrameSent;

    public bool IsOpen { get; private set; }
    public CanDeviceInfo? DeviceInfo { get; private set; }
    public CanProtocol ActiveProtocol { get; private set; } = CanProtocol.Can;

    public Task OpenAsync(CanDeviceOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ZLG CAN 设备仅支持 Windows。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        ZlgNative.ConfigureSearchPath();
        _options = options;
        OpenDevice(options, cancellationToken);
        InitializeChannel(options);
        IsOpen = true;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveLoopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (!IsOpen && _deviceHandle == IntPtr.Zero)
        {
            return;
        }

        _receiveLoopCts.Cancel();
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_channelHandle != IntPtr.Zero)
        {
            ZlgNative.ZCAN_ResetCAN(_channelHandle);
            _channelHandle = IntPtr.Zero;
        }

        if (_deviceHandle != IntPtr.Zero)
        {
            if (ZlgNative.ZCAN_CloseDevice(_deviceHandle) != ZlgNative.StatusOk)
            {
                throw new InvalidOperationException("ZCAN_CloseDevice 失败，设备可能仍被占用。");
            }

            _deviceHandle = IntPtr.Zero;
        }

        IsOpen = false;
    }

    public Task SendAsync(CanFrame frame, CancellationToken cancellationToken)
    {
        if (!IsOpen || _channelHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("ZLG CAN 设备尚未打开。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (ActiveProtocol == CanProtocol.CanFd && frame.IsCanFd)
        {
            SendCanFd(frame);
        }
        else
        {
            SendClassic(frame);
        }

        FrameSent?.Invoke(this, frame with
        {
            IsCanFd = ActiveProtocol == CanProtocol.CanFd && frame.IsCanFd,
            BitRateSwitch = ActiveProtocol == CanProtocol.CanFd && frame.BitRateSwitch
        });
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        _receiveLoopCts.Dispose();
    }

    private void OpenDevice(CanDeviceOptions options, CancellationToken cancellationToken)
    {
        var attempts = new List<string>();
        foreach (var descriptor in ZlgDeviceCatalog.ResolveCandidates(options.DeviceType))
        {
            foreach (var index in CandidateIndices(options.DeviceIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var handle = ZlgNative.ZCAN_OpenDevice(descriptor.DeviceType, index, 0);
                attempts.Add($"{descriptor.Model}[{index}]");
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                var nativeInfo = ZlgCanDeviceProbe.CreateDeviceInfo();
                if (ZlgNative.ZCAN_GetDeviceInf(handle, ref nativeInfo) != ZlgNative.StatusOk)
                {
                    nativeInfo.ChannelCount = (byte)descriptor.DefaultChannelCount;
                }

                _deviceHandle = handle;
                DeviceInfo = ZlgDeviceCatalog.CreateInfo(descriptor, index, nativeInfo);
                return;
            }
        }

        throw new InvalidOperationException(
            $"未检测到可用的 ZLG 设备。已尝试：{string.Join(", ", attempts)}。" +
            "请检查 USB 连接、驱动安装，并关闭其他占用 CAN 设备的软件。");
    }

    private void InitializeChannel(CanDeviceOptions options)
    {
        var channelCount = Math.Max(DeviceInfo?.ChannelCount ?? 1, 1);
        if (options.Channel >= channelCount)
        {
            CloseDeviceAfterOpenFailure();
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"设备只有 {channelCount} 个通道，无法打开 Channel {options.Channel}。");
        }

        ActiveProtocol = options.Protocol == CanProtocol.CanFd && DeviceInfo?.SupportsCanFd == true
            ? CanProtocol.CanFd
            : CanProtocol.Can;
        var init = ActiveProtocol == CanProtocol.CanFd
            ? BuildCanFdInit(options)
            : BuildClassicInit(options.BaudRate);
        _channelHandle = ZlgNative.ZCAN_InitCAN(_deviceHandle, options.Channel, ref init);
        if (_channelHandle == IntPtr.Zero)
        {
            CloseDeviceAfterOpenFailure();
            throw new InvalidOperationException(
                $"ZCAN_InitCAN 失败：Channel {options.Channel}，波特率 {options.BaudRate}。请检查通道和波特率配置。");
        }

        if (ZlgNative.ZCAN_ClearBuffer(_channelHandle) != ZlgNative.StatusOk
            || ZlgNative.ZCAN_StartCAN(_channelHandle) != ZlgNative.StatusOk)
        {
            CloseDeviceAfterOpenFailure();
            throw new InvalidOperationException($"ZLG Channel {options.Channel} 启动失败。");
        }
    }

    private ZlgNative.ChannelInitConfig BuildCanFdInit(CanDeviceOptions options)
    {
        var arbitrationPath = $"{options.Channel}/canfd_abit_baud_rate";
        var dataPath = $"{options.Channel}/canfd_dbit_baud_rate";
        var standardPath = $"{options.Channel}/canfd_standard";
        if (ZlgNative.ZCAN_SetValue(_deviceHandle, standardPath, "0") != ZlgNative.StatusOk
            || ZlgNative.ZCAN_SetValue(_deviceHandle, arbitrationPath, options.BaudRate.ToString()) != ZlgNative.StatusOk
            || ZlgNative.ZCAN_SetValue(_deviceHandle, dataPath, options.CanFdDataBaudRate.ToString()) != ZlgNative.StatusOk)
        {
            CloseDeviceAfterOpenFailure();
            throw new InvalidOperationException(
                $"CAN FD 波特率配置失败：仲裁段 {options.BaudRate}，数据段 {options.CanFdDataBaudRate}。");
        }

        return new ZlgNative.ChannelInitConfig
        {
            CanType = ZlgNative.TypeCanFd,
            AccCode = 0,
            AccMask = uint.MaxValue,
            CanFdFilter = 1,
            CanFdMode = 0
        };
    }

    private static ZlgNative.ChannelInitConfig BuildClassicInit(uint baudRate)
    {
        var (timing0, timing1) = baudRate switch
        {
            10_000 => ((byte)0x31, (byte)0x1C),
            20_000 => ((byte)0x18, (byte)0x1C),
            40_000 => ((byte)0x87, (byte)0xFF),
            50_000 => ((byte)0x09, (byte)0x1C),
            80_000 => ((byte)0x83, (byte)0xFF),
            100_000 => ((byte)0x04, (byte)0x1C),
            125_000 => ((byte)0x03, (byte)0x1C),
            200_000 => ((byte)0x81, (byte)0xFA),
            250_000 => ((byte)0x01, (byte)0x1C),
            400_000 => ((byte)0x80, (byte)0xFA),
            500_000 => ((byte)0x00, (byte)0x1C),
            666_000 => ((byte)0x80, (byte)0xB6),
            800_000 => ((byte)0x00, (byte)0x16),
            1_000_000 => ((byte)0x00, (byte)0x14),
            _ => throw new ArgumentOutOfRangeException(nameof(baudRate), $"不支持的经典 CAN 波特率：{baudRate}。")
        };
        return new ZlgNative.ChannelInitConfig
        {
            CanType = ZlgNative.TypeCan,
            AccCode = 0,
            AccMask = uint.MaxValue,
            ClassicFilter = 1,
            Timing0 = timing0,
            Timing1 = timing1,
            ClassicMode = 0
        };
    }

    private void SendClassic(CanFrame frame)
    {
        if (frame.Data.Length > 8)
        {
            throw new InvalidOperationException("经典 CAN 数据长度不能超过 8 字节。");
        }

        var data = new byte[8];
        Array.Copy(frame.Data, data, frame.Data.Length);
        var packet = new ZlgNative.ClassicTransmitData
        {
            Frame = new ZlgNative.ClassicFrame
            {
                CanId = ZlgNative.MakeCanId(frame.Id, frame.IsExtended, frame.IsRemote),
                Dlc = (byte)frame.Data.Length,
                Data = data
            }
        };
        if (ZlgNative.ZCAN_Transmit(_channelHandle, [packet], 1) != 1)
        {
            throw new InvalidOperationException("ZCAN_Transmit 发送失败。");
        }
    }

    private void SendCanFd(CanFrame frame)
    {
        if (frame.IsRemote)
        {
            throw new InvalidOperationException("CAN FD 不支持远程帧。");
        }

        if (frame.Data.Length > 64)
        {
            throw new InvalidOperationException("CAN FD 数据长度不能超过 64 字节。");
        }

        var data = new byte[64];
        Array.Copy(frame.Data, data, frame.Data.Length);
        var flags = frame.BitRateSwitch && _options.UseBitRateSwitch ? ZlgNative.CanFdBrs : (byte)0;
        var packet = new ZlgNative.CanFdTransmitData
        {
            Frame = new ZlgNative.CanFdFrame
            {
                CanId = ZlgNative.MakeCanId(frame.Id, frame.IsExtended, false),
                Length = (byte)frame.Data.Length,
                Flags = flags,
                Data = data
            }
        };
        if (ZlgNative.ZCAN_TransmitFD(_channelHandle, [packet], 1) != 1)
        {
            throw new InvalidOperationException("ZCAN_TransmitFD 发送失败。");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ReceiveClassic();
            if (DeviceInfo?.SupportsCanFd == true)
            {
                ReceiveCanFd();
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ReceiveClassic()
    {
        var buffer = CreateClassicReceiveBuffer(100);
        var count = ZlgNative.ZCAN_Receive(_channelHandle, buffer, (uint)buffer.Length, 0);
        if (count > buffer.Length)
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var item = buffer[index];
            var extended = (item.Frame.CanId & ZlgNative.CanEffFlag) != 0;
            var id = item.Frame.CanId & (extended ? ZlgNative.CanEffMask : ZlgNative.CanSffMask);
            FrameReceived?.Invoke(this, new CanFrame(
                id,
                item.Frame.Data.Take(item.Frame.Dlc).ToArray(),
                _options.Channel,
                extended,
                (item.Frame.CanId & ZlgNative.CanRtrFlag) != 0,
                TimestampFromMicroseconds(item.Timestamp)));
        }
    }

    private void ReceiveCanFd()
    {
        var buffer = CreateCanFdReceiveBuffer(100);
        var count = ZlgNative.ZCAN_ReceiveFD(_channelHandle, buffer, (uint)buffer.Length, 0);
        if (count > buffer.Length)
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var item = buffer[index];
            var extended = (item.Frame.CanId & ZlgNative.CanEffFlag) != 0;
            var id = item.Frame.CanId & (extended ? ZlgNative.CanEffMask : ZlgNative.CanSffMask);
            FrameReceived?.Invoke(this, new CanFrame(
                id,
                item.Frame.Data.Take(item.Frame.Length).ToArray(),
                _options.Channel,
                extended,
                false,
                TimestampFromMicroseconds(item.Timestamp),
                true,
                (item.Frame.Flags & ZlgNative.CanFdBrs) != 0,
                (item.Frame.Flags & ZlgNative.CanFdEsi) != 0));
        }
    }

    private void CloseDeviceAfterOpenFailure()
    {
        if (_channelHandle != IntPtr.Zero)
        {
            ZlgNative.ZCAN_ResetCAN(_channelHandle);
            _channelHandle = IntPtr.Zero;
        }

        if (_deviceHandle != IntPtr.Zero)
        {
            ZlgNative.ZCAN_CloseDevice(_deviceHandle);
            _deviceHandle = IntPtr.Zero;
        }
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

    private static DateTimeOffset? TimestampFromMicroseconds(ulong microseconds) =>
        microseconds == 0 ? null : DateTimeOffset.UtcNow;

    private static ZlgNative.ClassicReceiveData[] CreateClassicReceiveBuffer(int length)
    {
        var buffer = new ZlgNative.ClassicReceiveData[length];
        for (var index = 0; index < buffer.Length; index++)
        {
            buffer[index].Frame.Data = new byte[8];
        }

        return buffer;
    }

    private static ZlgNative.CanFdReceiveData[] CreateCanFdReceiveBuffer(int length)
    {
        var buffer = new ZlgNative.CanFdReceiveData[length];
        for (var index = 0; index < buffer.Length; index++)
        {
            buffer[index].Frame.Data = new byte[64];
        }

        return buffer;
    }
}
