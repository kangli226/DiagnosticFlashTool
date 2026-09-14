using DiagnosticFlashTool.Application;
using DiagnosticFlashTool.Core.Can;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
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
            Assert.Contains(result.LogMessages, line => line.Contains("Flash completed", StringComparison.Ordinal));
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

        Assert.Contains("reconnect", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsMissingRequiredApplication()
    {
        var options = CreateOptions(new CanDeviceOptions { DeviceType = "Mock" }, null);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Application", exception.Message, StringComparison.Ordinal);
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
                S3ClientMs = 0,
                PendingOverallTimeoutMs = 1000
            },
            CancellationToken.None);

        Assert.Equal(new byte[] { 0x62, 0xF1, 0x90 }, response.Payload);
    }

    private static FlashSessionOptions CreateOptions(CanDeviceOptions device, string? applicationPath)
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
                S3ClientMs = 0,
                PendingOverallTimeoutMs = 3000
            },
            ApplicationFilePaths = applicationPath is null ? [] : [applicationPath],
            ApplicationFallbackAddress = 0x00400000,
            RequireApplicationFile = true
        };
    }

    private sealed class RecordingProgress : IProgress<DiagnosticFlashTool.Core.Flashing.FlashProgress>
    {
        public List<int> Values { get; } = [];

        public void Report(DiagnosticFlashTool.Core.Flashing.FlashProgress value) => Values.Add(value.Percent);
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
            if ((frame.Data[0] >> 4) == 0 && frame.Data.Length > 1 && frame.Data[1] == 0x22)
            {
                FrameReceived?.Invoke(this, new CanFrame(0x708, [0x03, 0x7F, 0x22, 0x78, 0, 0, 0, 0]));
                FrameReceived?.Invoke(this, new CanFrame(0x708, [0x03, 0x62, 0xF1, 0x90, 0, 0, 0, 0]));
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
