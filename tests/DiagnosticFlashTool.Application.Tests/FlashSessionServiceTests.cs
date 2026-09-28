using DiagnosticFlashTool.Application;
using DiagnosticFlashTool.Core.Can;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;
using DiagnosticFlashTool.Core.Flashing;
using DiagnosticFlashTool.Infrastructure.Can;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

public sealed class FlashSessionServiceTests
{
    [Fact]
    public async Task FlashAsync_CompletesRepresentativeFlow_WithMockDevice()
    {
        var firmwarePath = Path.Combine(Path.GetTempPath(), $"ecu-flash-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(firmwarePath, Enumerable.Range(0, 64).Select(value => (byte)value).ToArray());

        await using var session = new FlashSessionService();
        try
        {
            var device = new CanDeviceOptions
            {
                DeviceType = "Mock",
                BaudRate = 500_000,
                Channel = 1
            };
            await session.ConnectAsync(device);

            var progress = new RecordingProgress();
            var result = await session.FlashAsync(CreateOptions(device, firmwarePath), progress);

            Assert.True(result.Success, result.UserMessage);
            Assert.Contains(result.LogMessages, line => line.Contains("刷写完成", StringComparison.Ordinal));
            Assert.Contains(result.LogMessages, line => line.Contains("P2ServerMax=50ms", StringComparison.Ordinal));
            Assert.Equal(100, progress.Values[^1]);
            Assert.True(progress.Values.SequenceEqual(progress.Values.OrderBy(value => value)));
        }
        finally
        {
            File.Delete(firmwarePath);
        }
    }

    [Fact]
    public async Task ConnectAsync_RequiresReconnect_WhenConnectionParametersChange()
    {
        await using var session = new FlashSessionService();
        await session.ConnectAsync(new CanDeviceOptions
        {
            DeviceType = "Mock",
            BaudRate = 500_000,
            Channel = 0
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConnectAsync(new CanDeviceOptions
        {
            DeviceType = "Mock",
            BaudRate = 500_000,
            Channel = 1
        }));

        Assert.Contains("重新连接", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsMissingRequiredApplication()
    {
        var options = CreateOptions(new CanDeviceOptions { DeviceType = "Mock" }, null);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("应用固件", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timing_RejectsNonPositiveP2()
    {
        var timing = new UdsTimingOptions { P2ClientMs = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(timing.Validate);
    }

    [Fact]
    public async Task UdsClient_ConsumesFinalResponseBufferedImmediatelyAfterPending()
    {
        await using var device = new ImmediatePendingCanDevice();
        await device.OpenAsync(new CanDeviceOptions { DeviceType = "Mock" }, CancellationToken.None);
        using var transport = new IsoTpTransport(device, new DiagnosticTransportOptions
        {
            PhysicalRequestId = 0x700,
            FunctionalRequestId = 0x7DF,
            ResponseId = 0x708
        });
        var client = new UdsClient(transport);

        var response = await client.SendAsync(
            0x22,
            [0xF1, 0x90],
            UdsAddressing.Physical,
            new UdsTimingOptions
            {
                P2ClientMs = 250,
                P2StarClientMs = 250,
                S3ServerTimeoutMs = 0,
                TesterPresentIntervalMs = 0,
                PendingOverallTimeoutMs = 1000
            },
            CancellationToken.None);

        Assert.Equal(new byte[] { 0x62, 0xF1, 0x90 }, response.Payload);
    }

    [Fact]
    public async Task UdsClient_ConsumesSingleResponseReceivedDuringSend()
    {
        await using var device = new ImmediatePendingCanDevice();
        await device.OpenAsync(new CanDeviceOptions { DeviceType = "Mock" }, CancellationToken.None);
        using var transport = new IsoTpTransport(device, new DiagnosticTransportOptions
        {
            PhysicalRequestId = 0x700,
            FunctionalRequestId = 0x7DF,
            ResponseId = 0x708
        });
        var client = new UdsClient(transport);

        var response = await client.SendAsync(
            0x10,
            [0x02],
            UdsAddressing.Physical,
            new UdsTimingOptions
            {
                P2ClientMs = 250,
                P2StarClientMs = 250,
                S3ServerTimeoutMs = 0,
                TesterPresentIntervalMs = 0,
                PendingOverallTimeoutMs = 1000
            },
            CancellationToken.None);

        Assert.Equal(new byte[] { 0x50, 0x02 }, response.Payload);
    }

    [Fact]
    public async Task FlashAsync_WeightsProgressByFirmwareSize_NotByStepCount()
    {
        var firmwarePath = Path.Combine(Path.GetTempPath(), $"ecu-flash-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(firmwarePath, new byte[4096]);

        await using var session = new FlashSessionService();
        try
        {
            var device = new CanDeviceOptions { DeviceType = "Mock", BaudRate = 500_000, Channel = 1 };
            await session.ConnectAsync(device);

            var progress = new RecordingProgress();
            var result = await session.FlashAsync(CreateOptions(device, firmwarePath), progress);

            Assert.True(result.Success, result.UserMessage);

            // Four cheap UDS steps run before the download. Dividing the bar equally by step
            // count would already put it at 60% when the download starts, so it would sit
            // still for the whole transfer. Weighting by bytes keeps it well below that.
            var downloadStart = progress.Entries
                .First(entry => entry.Message.Contains("应用固件下载", StringComparison.Ordinal));

            Assert.True(
                downloadStart.Percent < 40,
                $"下载开始时进度已经是 {downloadStart.Percent}%，说明进度没有按数据量加权。");
        }
        finally
        {
            File.Delete(firmwarePath);
        }
    }

    [Fact]
    public async Task FlashAsync_FailsWhenFlowDeclaresDownloadButNoFirmwareIsConfigured()
    {
        await using var session = new FlashSessionService();
        var device = new CanDeviceOptions { DeviceType = "Mock", BaudRate = 500_000, Channel = 1 };
        await session.ConnectAsync(device);

        // The flow is the specification: a DownloadApplication step with no file has to
        // fail, rather than report success without having written anything.
        var options = CreateOptions(device, null, requireApplicationFile: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.FlashAsync(options));

        Assert.Contains("DownloadApplication", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CancelDuringDownload_ExitsTransferAndReturnsToDefaultSession()
    {
        using var cancellation = new CancellationTokenSource();
        await using var device = new StallingCanDevice(cancellation);
        await device.OpenAsync(new CanDeviceOptions { DeviceType = "Mock" }, CancellationToken.None);

        using var transport = new IsoTpTransport(device, new DiagnosticTransportOptions
        {
            PhysicalRequestId = 0x700,
            FunctionalRequestId = 0x7DF,
            ResponseId = 0x708,
            Channel = 0,
            ExtendedFrame = false
        });
        var executor = new FlashFlowExecutor(new UdsClient(transport));

        var bootConfig = new BootConfig
        {
            Name = "cancel",
            Flow =
            [
                new FlashStepConfig { Id = 1, Name = "Programming session", Service = "0x10", SubService = "0x02", AddressingMode = "physical" },
                new FlashStepConfig
                {
                    Id = 2,
                    Name = "Download application",
                    StepType = "DownloadApplication",
                    AddressingMode = "physical",
                    BlockSize = "0x05"
                }
            ]
        };

        var firmwareSet = new FirmwareSet
        {
            Application = new FirmwareImage
            {
                FilePath = "in-memory",
                Kind = FirmwareImageKind.Application,
                Blocks = [new FirmwareBlock(0x00400000, new byte[4096])]
            }
        };

        var exception = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            new FlashExecutionRequest(bootConfig, firmwareSet),
            null,
            cancellation.Token));

        Assert.True(
            exception is OperationCanceledException,
            $"期望取消异常，实际为：{exception?.ToString() ?? "（未抛出异常）"}");

        var sent = device.SentSingleFramePayloads();

        // The transfer must be closed, otherwise the ECU keeps waiting for TransferData.
        Assert.Contains(sent, payload => payload.Length > 0 && payload[0] == 0x37);

        // And the ECU must not be left inside the programming session.
        Assert.Contains(sent, payload => payload.Length > 1 && payload[0] == 0x10 && payload[1] == 0x01);
    }

    [Fact]
    public async Task ExecuteAsync_WeightsMultipleApplicationsByBytes()
    {
        await using var device = new MockCanDevice();
        var transportOptions = new DiagnosticTransportOptions
        {
            PhysicalRequestId = 0x700,
            FunctionalRequestId = 0x7DF,
            ResponseId = 0x708,
            Channel = 0,
            ExtendedFrame = false
        };
        device.ConfigureTransport(transportOptions);
        await device.OpenAsync(new CanDeviceOptions { DeviceType = "Mock" }, CancellationToken.None);

        using var transport = new IsoTpTransport(device, transportOptions);
        IFlashFlowExecutor executor = new FlashFlowExecutor(new UdsClient(transport));
        var bootConfig = new BootConfig
        {
            Name = "multiple-applications",
            Flow =
            [
                new FlashStepConfig
                {
                    Id = 1,
                    Name = "Download applications",
                    StepType = "DownloadApplication",
                    AddressingMode = "physical",
                    BlockSize = "0x40"
                }
            ]
        };
        var firmwareSet = new FirmwareSet
        {
            Applications =
            [
                new FirmwareImage
                {
                    FilePath = "small.bin",
                    Kind = FirmwareImageKind.Application,
                    Blocks = [new FirmwareBlock(0x00400000, new byte[32])]
                },
                new FirmwareImage
                {
                    FilePath = "large.bin",
                    Kind = FirmwareImageKind.Application,
                    Blocks = [new FirmwareBlock(0x00500000, new byte[1024])]
                }
            ]
        };
        var progress = new RecordingProgress();

        var result = await executor.ExecuteAsync(
            new FlashExecutionRequest(bootConfig, firmwareSet),
            progress);

        Assert.True(result.Success, result.UserMessage);
        var firstDownloadProgress = progress.Entries.First(entry =>
            entry.Percent > 0 && entry.Message.Contains("应用固件下载", StringComparison.Ordinal));
        Assert.InRange(firstDownloadProgress.Percent, 1, 5);
        Assert.True(progress.Values.SequenceEqual(progress.Values.OrderBy(value => value)));
    }

    [Theory]
    [InlineData("0x10", "0x01", 1)]
    [InlineData("0x11", "0x01", 0)]
    public async Task ExecuteAsync_DoesNotRepeatSessionCleanup_WhenFlowAlreadyLeftProgrammingSession(
        string service,
        string subService,
        int expectedDefaultSessionRequests)
    {
        using var cancellation = new CancellationTokenSource();
        await using var device = new StallingCanDevice(cancellation);
        await device.OpenAsync(new CanDeviceOptions { DeviceType = "Mock" }, CancellationToken.None);

        using var transport = new IsoTpTransport(device, new DiagnosticTransportOptions
        {
            PhysicalRequestId = 0x700,
            FunctionalRequestId = 0x7DF,
            ResponseId = 0x708,
            Channel = 0,
            ExtendedFrame = false
        });
        IFlashFlowExecutor executor = new FlashFlowExecutor(new UdsClient(transport));
        var bootConfig = new BootConfig
        {
            Name = "default-session",
            Flow =
            [
                new FlashStepConfig { Id = 1, Name = "Programming session", Service = "0x10", SubService = "0x02" },
                new FlashStepConfig { Id = 2, Name = "Leave programming session", Service = service, SubService = subService },
                new FlashStepConfig
                {
                    Id = 3,
                    Name = "Download application",
                    StepType = "DownloadApplication",
                    BlockSize = "0x05"
                }
            ]
        };
        var firmwareSet = new FirmwareSet
        {
            Application = new FirmwareImage
            {
                FilePath = "in-memory",
                Kind = FirmwareImageKind.Application,
                Blocks = [new FirmwareBlock(0x00400000, new byte[64])]
            }
        };

        var exception = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            new FlashExecutionRequest(bootConfig, firmwareSet),
            cancellationToken: cancellation.Token));

        Assert.True(
            exception is OperationCanceledException,
            $"期望取消异常，实际为：{exception?.ToString() ?? "（未抛出异常）"}");

        var defaultSessionRequests = device.SentSingleFramePayloads()
            .Count(payload => payload.Length > 1 && payload[0] == 0x10 && payload[1] == 0x01);
        Assert.Equal(expectedDefaultSessionRequests, defaultSessionRequests);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsConfigurationFailure_WhenDownloadFirmwareIsMissing()
    {
        await using var device = new MockCanDevice();
        var transportOptions = new DiagnosticTransportOptions
        {
            PhysicalRequestId = 0x700,
            FunctionalRequestId = 0x7DF,
            ResponseId = 0x708,
            Channel = 0,
            ExtendedFrame = false
        };
        device.ConfigureTransport(transportOptions);
        await device.OpenAsync(new CanDeviceOptions { DeviceType = "Mock" }, CancellationToken.None);

        using var transport = new IsoTpTransport(device, transportOptions);
        IFlashFlowExecutor executor = new FlashFlowExecutor(new UdsClient(transport));
        var result = await executor.ExecuteAsync(new FlashExecutionRequest(
            new BootConfig
            {
                Name = "missing-firmware",
                Flow =
                [
                    new FlashStepConfig
                    {
                        Id = 1,
                        Name = "Download application",
                        StepType = "DownloadApplication"
                    }
                ]
            },
            new FirmwareSet()));

        Assert.False(result.Success);
        Assert.Equal(FlashFailureKind.Configuration, result.FailureKind);
        Assert.Contains("应用固件", result.UserMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WaitsForCurrentStepAndOnlyStartsNextStepAfterSuccess(
        bool firstStepSucceeds)
    {
        await using var device = new ControlledSequenceCanDevice();
        await device.OpenAsync(new CanDeviceOptions { DeviceType = "Mock" }, CancellationToken.None);
        using var transport = new IsoTpTransport(device, new DiagnosticTransportOptions
        {
            PhysicalRequestId = 0x700,
            FunctionalRequestId = 0x7DF,
            ResponseId = 0x708
        });
        IFlashFlowExecutor executor = new FlashFlowExecutor(new UdsClient(transport));
        var execution = executor.ExecuteAsync(new FlashExecutionRequest(
            new BootConfig
            {
                Name = "sequential",
                Flow =
                [
                    new FlashStepConfig { Id = 1, Name = "Programming session", Service = "0x10", SubService = "0x02" },
                    new FlashStepConfig { Id = 2, Name = "ECU reset", Service = "0x11", SubService = "0x01" }
                ]
            },
            new FirmwareSet()));

        await device.FirstRequestReceived.WaitAsync(TimeSpan.FromSeconds(1));

        // 第一步尚未响应时，第二步不能提前发送。
        Assert.Equal(new byte[] { 0x10 }, device.SentServices);

        device.CompleteFirstRequest(firstStepSucceeds);
        var result = await execution;

        if (firstStepSucceeds)
        {
            Assert.True(result.Success, result.UserMessage);
            Assert.Equal(new byte[] { 0x10, 0x11 }, device.SentServices);
        }
        else
        {
            Assert.False(result.Success);
            Assert.Equal(FlashFailureKind.Protocol, result.FailureKind);
            Assert.Equal(new byte[] { 0x10 }, device.SentServices);
        }
    }

    private static FlashSessionOptions CreateOptions(
        CanDeviceOptions device,
        string? applicationPath,
        bool requireApplicationFile = true)
    {
        return new FlashSessionOptions
        {
            BootConfig = new BootConfig
            {
                Name = "ECU-X Mock",
                Flow =
                [
                    new FlashStepConfig { Id = 1, Name = "Programming session", Service = "0x10", SubService = "0x02", AddressingMode = "physical" },
                    new FlashStepConfig { Id = 2, Name = "Request seed", Service = "0x27", SubService = "0x01", AddressingMode = "physical" },
                    new FlashStepConfig { Id = 3, Name = "Send key", Service = "0x27", SubService = "0x02", SecurityAlgorithm = "ECU_DLL", AddressingMode = "physical" },
                    new FlashStepConfig { Id = 4, Name = "Download application", StepType = "DownloadApplication", AddressingMode = "physical" },
                    new FlashStepConfig { Id = 5, Name = "Reset", Service = "0x11", SubService = "0x01", AddressingMode = "physical" }
                ]
            },
            Project = new ProjectConfigEntry { ProjectName = "ECU-X Mock" },
            Device = device,
            Transport = new DiagnosticTransportOptions
            {
                PhysicalRequestId = 0x700,
                FunctionalRequestId = 0x7DF,
                ResponseId = 0x708,
                Channel = device.Channel,
                ExtendedFrame = false
            },
            Timing = new UdsTimingOptions
            {
                P2ClientMs = 1000,
                P2StarClientMs = 1000,
                S3ServerTimeoutMs = 0,
                TesterPresentIntervalMs = 0,
                PendingOverallTimeoutMs = 3000
            },
            ApplicationFilePaths = applicationPath is null ? [] : [applicationPath],
            ApplicationFallbackAddress = 0x00400000,
            RequireApplicationFile = requireApplicationFile
        };
    }

    private sealed class RecordingProgress : IProgress<DiagnosticFlashTool.Core.Flashing.FlashProgress>
    {
        public List<int> Values { get; } = [];

        public List<(int Percent, string Message)> Entries { get; } = [];

        public void Report(DiagnosticFlashTool.Core.Flashing.FlashProgress value)
        {
            Values.Add(value.Percent);
            Entries.Add((value.Percent, value.Message));
        }
    }

    /// <summary>
    /// Answers RequestDownload and RequestTransferExit, but cancels the flash as soon as the
    /// first TransferData frame arrives, so the cleanup path after a cancellation can be
    /// observed without timing dependencies.
    /// </summary>
    private sealed class StallingCanDevice(CancellationTokenSource cancellation) : ICanDevice
    {
        private const uint ResponseId = 0x708;
        private readonly List<CanFrame> _sent = [];
        private readonly object _rxSync = new();
        private int _expectedLength;
        private List<byte>? _rxBuffer;
        private byte _nextSequence = 1;

        public event EventHandler<CanFrame>? FrameReceived;
        public event EventHandler<CanFrame>? FrameSent;

        public bool IsOpen { get; private set; }

        public Task OpenAsync(CanDeviceOptions options, CancellationToken cancellationToken)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public Task SendAsync(CanFrame frame, CancellationToken cancellationToken)
        {
            lock (_sent)
            {
                _sent.Add(frame);
            }

            FrameSent?.Invoke(this, frame);

            if (frame.Data.Length == 0)
            {
                return Task.CompletedTask;
            }

            switch (frame.Data[0] >> 4)
            {
                case 0:
                    Respond(frame.Data.Skip(1).Take(frame.Data[0] & 0x0F).ToArray());
                    break;
                case 1:
                    lock (_rxSync)
                    {
                        _expectedLength = ((frame.Data[0] & 0x0F) << 8) | frame.Data[1];
                        _rxBuffer = frame.Data.Skip(2).Take(Math.Min(6, _expectedLength)).ToList();
                        _nextSequence = 1;
                    }

                    EmitRaw([0x30, 0x00, 0x00]);
                    break;
                case 2:
                    byte[]? completed = null;
                    lock (_rxSync)
                    {
                        if (_rxBuffer is null || _expectedLength <= 0
                            || (byte)(frame.Data[0] & 0x0F) != _nextSequence)
                        {
                            return Task.CompletedTask;
                        }

                        _nextSequence = (byte)((_nextSequence + 1) & 0x0F);
                        _rxBuffer.AddRange(frame.Data.Skip(1));
                        if (_rxBuffer.Count >= _expectedLength)
                        {
                            completed = _rxBuffer.Take(_expectedLength).ToArray();
                            _rxBuffer = null;
                            _expectedLength = 0;
                        }
                    }

                    if (completed is not null)
                    {
                        Respond(completed);
                    }

                    break;
            }

            return Task.CompletedTask;
        }

        /// <summary>Single-frame payloads the client transmitted, in order.</summary>
        public IReadOnlyList<byte[]> SentSingleFramePayloads()
        {
            lock (_sent)
            {
                return _sent
                    .Where(frame => frame.Data.Length > 0 && (frame.Data[0] >> 4) == 0)
                    .Select(frame => frame.Data.Skip(1).Take(frame.Data[0] & 0x0F).ToArray())
                    .ToList();
            }
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        private void Respond(byte[] payload)
        {
            switch (payload.Length > 0 ? payload[0] : 0)
            {
                case 0x34:
                    EmitPayload([0x74, 0x20, 0x0F, 0x00]);
                    break;
                case 0x37:
                case 0x10:
                case 0x11:
                    EmitPayload([(byte)(payload[0] + 0x40), payload.Length > 1 ? payload[1] : (byte)0x00]);
                    break;
                case 0x36:
                    // 不返回 0x36 响应，通过取消令牌触发下载中止和清理路径。
                    cancellation.Cancel();
                    break;
            }
        }

        private void EmitPayload(byte[] payload)
        {
            var data = new byte[8];
            data[0] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, data, 1, payload.Length);
            EmitRaw(data);
        }

        private void EmitRaw(byte[] data)
        {
            FrameReceived?.Invoke(this, new CanFrame(ResponseId, data, 0, false));
        }
    }

    private sealed class ImmediatePendingCanDevice : ICanDevice
    {
        public event EventHandler<CanFrame>? FrameReceived;
        public event EventHandler<CanFrame>? FrameSent;

        public bool IsOpen { get; private set; }

        public Task OpenAsync(CanDeviceOptions options, CancellationToken cancellationToken)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public Task SendAsync(CanFrame frame, CancellationToken cancellationToken)
        {
            FrameSent?.Invoke(this, frame);
            if ((frame.Data[0] >> 4) != 0 || frame.Data.Length <= 1)
            {
                return Task.CompletedTask;
            }

            if (frame.Data[1] == 0x22)
            {
                FrameReceived?.Invoke(this, new CanFrame(0x708, [0x03, 0x7F, 0x22, 0x78, 0, 0, 0, 0]));
                FrameReceived?.Invoke(this, new CanFrame(0x708, [0x03, 0x62, 0xF1, 0x90, 0, 0, 0, 0]));
            }
            else if (frame.Data[1] == 0x10)
            {
                FrameReceived?.Invoke(this, new CanFrame(0x708, [0x02, 0x50, 0x02, 0, 0, 0, 0, 0]));
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledSequenceCanDevice : ICanDevice
    {
        private const uint ResponseId = 0x708;
        private readonly object _sync = new();
        private readonly List<byte> _sentServices = [];
        private readonly TaskCompletionSource _firstRequestReceived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? _firstRequest;

        public event EventHandler<CanFrame>? FrameReceived;
        public event EventHandler<CanFrame>? FrameSent;

        public bool IsOpen { get; private set; }

        public Task FirstRequestReceived => _firstRequestReceived.Task;

        public IReadOnlyList<byte> SentServices
        {
            get
            {
                lock (_sync)
                {
                    return _sentServices.ToArray();
                }
            }
        }

        public Task OpenAsync(CanDeviceOptions options, CancellationToken cancellationToken)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public Task SendAsync(CanFrame frame, CancellationToken cancellationToken)
        {
            FrameSent?.Invoke(this, frame);
            if ((frame.Data[0] >> 4) != 0 || frame.Data.Length <= 1)
            {
                return Task.CompletedTask;
            }

            var payload = frame.Data.Skip(1).Take(frame.Data[0] & 0x0F).ToArray();
            var isFirstRequest = false;
            lock (_sync)
            {
                _sentServices.Add(payload[0]);
                if (_firstRequest is null)
                {
                    _firstRequest = payload;
                    isFirstRequest = true;
                }
            }

            if (isFirstRequest)
            {
                _firstRequestReceived.TrySetResult();
            }
            else
            {
                EmitPositiveResponse(payload);
            }

            return Task.CompletedTask;
        }

        public void CompleteFirstRequest(bool success)
        {
            byte[] request;
            lock (_sync)
            {
                request = _firstRequest?.ToArray()
                    ?? throw new InvalidOperationException("尚未收到第一条请求。");
            }

            if (success)
            {
                EmitPositiveResponse(request);
                return;
            }

            EmitPayload([0x7F, request[0], 0x22]);
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        private void EmitPositiveResponse(byte[] request)
        {
            var response = request.Length > 1
                ? new byte[] { (byte)(request[0] + 0x40), request[1] }
                : [(byte)(request[0] + 0x40)];
            EmitPayload(response);
        }

        private void EmitPayload(byte[] payload)
        {
            var data = new byte[8];
            data[0] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, data, 1, payload.Length);
            FrameReceived?.Invoke(this, new CanFrame(ResponseId, data, 0, false));
        }
    }
}
