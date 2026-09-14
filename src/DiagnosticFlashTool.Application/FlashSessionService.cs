using DiagnosticFlashTool.Core.Algorithms;
using DiagnosticFlashTool.Core.Can;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;
using DiagnosticFlashTool.Core.Flashing;
using DiagnosticFlashTool.Infrastructure.Can;
using DiagnosticFlashTool.Infrastructure.Security;

namespace DiagnosticFlashTool.Application;

/// <summary>
/// Coordinates device lifetime, ISO-TP/UDS transport, Tester Present and the
/// Core flash executor. The service is UI-agnostic and can be hosted by the
/// existing full application or by a product-specific WPF shell.
/// </summary>
public sealed class FlashSessionService : IAsyncDisposable
{
    private readonly CanDeviceFactory _deviceFactory;
    private readonly FirmwareLoader _firmwareLoader;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ICanDevice? _device;
    private CanDeviceOptions? _connectedOptions;
    private bool _disposed;

    public FlashSessionService(
        CanDeviceFactory? deviceFactory = null,
        FirmwareLoader? firmwareLoader = null)
    {
        _deviceFactory = deviceFactory ?? new CanDeviceFactory();
        _firmwareLoader = firmwareLoader ?? new FirmwareLoader();
    }

    public event EventHandler<CanFrame>? FrameReceived;
    public event EventHandler<CanFrame>? FrameSent;

    public bool IsConnected => _device?.IsOpen == true;

    public async Task ConnectAsync(CanDeviceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsConnected)
            {
                if (ConnectionMatches(options, _connectedOptions))
                {
                    return;
                }

                throw new InvalidOperationException("CAN connection parameters changed. Disconnect and reconnect the device before flashing.");
            }

            if (_device is not null)
            {
                await DisconnectCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var device = _deviceFactory.Create(options.DeviceType);
            device.FrameReceived += ForwardFrameReceived;
            device.FrameSent += ForwardFrameSent;
            try
            {
                await device.OpenAsync(options, cancellationToken).ConfigureAwait(false);
                _device = device;
                _connectedOptions = CopyConnectionOptions(options);
            }
            catch
            {
                device.FrameReceived -= ForwardFrameReceived;
                device.FrameSent -= ForwardFrameSent;
                await device.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<FlashResult> FlashAsync(
        FlashSessionOptions options,
        IProgress<FlashProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var device = _device;
            if (device is null || !device.IsOpen)
            {
                throw new InvalidOperationException("CAN device is not connected.");
            }

            if (!ConnectionMatches(options.Device, _connectedOptions))
            {
                throw new InvalidOperationException("Flash device parameters do not match the active CAN connection. Disconnect and reconnect the device.");
            }

            if (device is MockCanDevice mockDevice)
            {
                mockDevice.ConfigureTransport(options.Transport);
            }

            var firmwareSet = await Task.Run(
                () => LoadFirmwareSet(options),
                cancellationToken).ConfigureAwait(false);
            using var transport = new IsoTpTransport(device, options.Transport);
            var udsClient = new UdsClient(transport);
            var algorithms = new SeedKeyAlgorithmRegistry();
            if (options.SeedKeyDll is not null)
            {
                algorithms.Register(new DllSeedKeyAlgorithm(options.SeedKeyDll));
            }
            else if (string.Equals(options.Device.DeviceType, "Mock", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var name in options.BootConfig.Flow
                             .Select(step => step.SecurityAlgorithm)
                             .Where(name => !string.IsNullOrWhiteSpace(name))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    algorithms.Register(new MockSeedKeyAlgorithm(name!));
                }
            }

            var testerPresent = CreateTesterPresentScheduler(udsClient, options.Timing);
            testerPresent?.Start();
            try
            {
                var executor = new FlashFlowExecutor(udsClient, algorithms);
                return await executor.ExecuteAsync(
                    options.BootConfig,
                    options.Project,
                    firmwareSet,
                    progress,
                    log,
                    cancellationToken,
                    options.Timing).ConfigureAwait(false);
            }
            finally
            {
                if (testerPresent is not null)
                {
                    await testerPresent.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private FirmwareSet LoadFirmwareSet(FlashSessionOptions options)
    {
        FirmwareImage? driver = null;
        if (!string.IsNullOrWhiteSpace(options.DriverFilePath))
        {
            driver = _firmwareLoader.Load(options.DriverFilePath, FirmwareImageKind.Driver, options.DriverFallbackAddress);
        }

        var applications = options.ApplicationFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => _firmwareLoader.Load(path, FirmwareImageKind.Application, options.ApplicationFallbackAddress))
            .ToList();

        return new FirmwareSet
        {
            Driver = driver,
            Applications = applications
        };
    }

    private static TesterPresentScheduler? CreateTesterPresentScheduler(UdsClient udsClient, UdsTimingOptions timing)
    {
        if (timing.S3ClientMs <= 0)
        {
            return null;
        }

        return new TesterPresentScheduler(
            udsClient,
            TimeSpan.FromMilliseconds(Math.Max(1, timing.S3ClientMs / 2)),
            UdsAddressing.Physical);
    }

    private async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        var device = _device;
        _device = null;
        _connectedOptions = null;
        if (device is null)
        {
            return;
        }

        device.FrameReceived -= ForwardFrameReceived;
        device.FrameSent -= ForwardFrameSent;
        try
        {
            await device.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await device.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ForwardFrameReceived(object? sender, CanFrame frame) => FrameReceived?.Invoke(this, frame);

    private void ForwardFrameSent(object? sender, CanFrame frame) => FrameSent?.Invoke(this, frame);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static CanDeviceOptions CopyConnectionOptions(CanDeviceOptions options)
    {
        return new CanDeviceOptions
        {
            DeviceType = options.DeviceType,
            DeviceIndex = options.DeviceIndex,
            Channel = options.Channel,
            BaudRate = options.BaudRate,
            AutoFlashEnabled = options.AutoFlashEnabled
        };
    }

    private static bool ConnectionMatches(CanDeviceOptions current, CanDeviceOptions? connected)
    {
        return connected is not null
            && string.Equals(current.DeviceType, connected.DeviceType, StringComparison.OrdinalIgnoreCase)
            && current.DeviceIndex == connected.DeviceIndex
            && current.Channel == connected.Channel
            && current.BaudRate == connected.BaudRate;
    }

    private sealed class MockSeedKeyAlgorithm(string name) : ISeedKeyAlgorithm
    {
        public string Name { get; } = name;

        public byte[] ComputeKey(byte[] seed, IReadOnlyList<string> parameters)
        {
            ArgumentNullException.ThrowIfNull(seed);
            return [.. seed];
        }
    }
}
