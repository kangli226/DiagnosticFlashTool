using DiagnosticFlashTool.Core.Algorithms;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Core.Flashing;

public sealed class FlashFlowExecutor
{
    private readonly UdsClient _udsClient;
    private readonly SeedKeyAlgorithmRegistry _seedKeyAlgorithms;
    private byte[] _lastSeed = [];

    public FlashFlowExecutor(UdsClient udsClient, SeedKeyAlgorithmRegistry? seedKeyAlgorithms = null)
    {
        _udsClient = udsClient;
        _seedKeyAlgorithms = seedKeyAlgorithms ?? new SeedKeyAlgorithmRegistry();
    }

    public async Task<FlashResult> ExecuteAsync(
        BootConfig bootConfig,
        ProjectConfigEntry project,
        FirmwareSet firmwareSet,
        IProgress<FlashProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken,
        UdsTimingOptions? timingOptions = null)
    {
        var timing = timingOptions?.Validate();
        _lastSeed = [];
        var logs = new List<string>();
        void WriteLog(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            logs.Add(line);
            log?.Invoke(line);
        }

        try
        {
            if (bootConfig.Flow.Count == 0)
            {
                throw new InvalidOperationException($"BOOT config has no flow steps: {bootConfig.Name}");
            }

            WriteLog($"Start flash: project={project.ProjectName}, boot={bootConfig.Name}");

            for (var index = 0; index < bootConfig.Flow.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = bootConfig.Flow[index];
                var stepStartPercent = index * 100.0 / bootConfig.Flow.Count;
                var stepEndPercent = (index + 1) * 100.0 / bootConfig.Flow.Count;
                void ReportStepProgress(int percentWithinStep, string message)
                {
                    var normalized = Math.Clamp(percentWithinStep, 0, 100) / 100.0;
                    var overall = (int)Math.Round(stepStartPercent + ((stepEndPercent - stepStartPercent) * normalized));
                    progress?.Report(new FlashProgress(Math.Clamp(overall, 0, 100), message));
                }

                ReportStepProgress(0, $"Step {step.Id}: {step.Name}");
                WriteLog($"Step {step.Id}: {step.Name}");

                if (string.Equals(step.StepType, "DownloadDriver", StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadImageAsync(firmwareSet.Driver, step, FirmwareImageKind.Driver, WriteLog, ReportStepProgress, cancellationToken, timing).ConfigureAwait(false);
                }
                else if (string.Equals(step.StepType, "DownloadApplication", StringComparison.OrdinalIgnoreCase))
                {
                    var applications = firmwareSet.GetApplicationImages();
                    if (applications.Count == 0)
                    {
                        await DownloadImageAsync(null, step, FirmwareImageKind.Application, WriteLog, ReportStepProgress, cancellationToken, timing).ConfigureAwait(false);
                    }
                    else
                    {
                        for (var applicationIndex = 0; applicationIndex < applications.Count; applicationIndex++)
                        {
                            var currentIndex = applicationIndex;
                            void ReportApplicationProgress(int imagePercent, string message)
                            {
                                var allImagesPercent = (int)Math.Round((currentIndex + (Math.Clamp(imagePercent, 0, 100) / 100.0)) * 100 / applications.Count);
                                ReportStepProgress(allImagesPercent, message);
                            }

                            await DownloadImageAsync(
                                applications[applicationIndex],
                                step,
                                FirmwareImageKind.Application,
                                WriteLog,
                                ReportApplicationProgress,
                                cancellationToken,
                                timing).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    await ExecuteUdsStepAsync(step, WriteLog, cancellationToken, timing).ConfigureAwait(false);
                }

                ReportStepProgress(100, $"Step {step.Id} completed: {step.Name}");
            }

            progress?.Report(new FlashProgress(100, "Flash completed"));
            WriteLog("Flash completed.");
            return new FlashResult { Success = true, UserMessage = "刷写完成", LogMessages = logs };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteLog("Flash canceled.");
            throw;
        }
        catch (Exception ex)
        {
            WriteLog($"ERROR: {ex.Message}");
            return new FlashResult { Success = false, UserMessage = ex.Message, LogMessages = logs };
        }
    }

    private async Task ExecuteUdsStepAsync(
        FlashStepConfig step,
        Action<string> log,
        CancellationToken cancellationToken,
        UdsTimingOptions? timing)
    {
        if (!HexUtil.TryParseByte(step.Service, out var serviceId))
        {
            log("Skip non-UDS step without service.");
            return;
        }

        var parameters = new List<byte>();
        if (HexUtil.TryParseByte(step.SubService, out var subService))
        {
            parameters.Add(subService);
        }

        parameters.AddRange(step.Extend.SelectMany(HexUtil.ParseBytes));

        if (serviceId == 0x27 && HexUtil.TryParseByte(step.SubService, out subService) && subService % 2 == 0 && !string.IsNullOrWhiteSpace(step.SecurityAlgorithm))
        {
            var algorithm = _seedKeyAlgorithms.Resolve(step.SecurityAlgorithm);
            if (_lastSeed.Length == 0)
            {
                throw new InvalidOperationException("Security key requested before a seed was received.");
            }

            var key = algorithm.ComputeKey(_lastSeed, step.AlgorithmParams.ToList());
            parameters.AddRange(key);
            log($"Security key generated by {algorithm.Name}, length={key.Length} bytes.");
        }

        var requestTiming = ResolveTiming(step, timing);

        var response = await _udsClient.SendAsync(
            serviceId,
            parameters,
            ParseAddressing(step.AddressingMode),
            requestTiming,
            cancellationToken).ConfigureAwait(false);

        response.EnsurePositive();
        log($"RX {response}");

        if (serviceId == 0x27 && response.Payload.Length > 2 && response.Payload[0] == 0x67 && response.Payload[1] % 2 == 1)
        {
            _lastSeed = response.Payload.Skip(2).ToArray();
        }
    }

    private async Task DownloadImageAsync(
        FirmwareImage? image,
        FlashStepConfig step,
        FirmwareImageKind kind,
        Action<string> log,
        Action<int, string>? reportProgress,
        CancellationToken cancellationToken,
        UdsTimingOptions? timing)
    {
        if (image is null || image.Length == 0)
        {
            log($"{kind} firmware not configured, skip download step.");
            reportProgress?.Invoke(100, $"{kind} firmware skipped");
            return;
        }

        var completedBytes = 0;
        foreach (var block in image.Blocks)
        {
            var requestTiming = ResolveTiming(step, timing);
            var ecuMaximumBlockLength = await RequestDownloadAsync(
                block.Address,
                block.Data.Length,
                step,
                requestTiming,
                cancellationToken).ConfigureAwait(false);

            var payloadSize = ResolveTransferDataSize(step, ecuMaximumBlockLength);
            var blockCounter = 1;
            for (var offset = 0; offset < block.Data.Length; offset += payloadSize)
            {
                var count = Math.Min(payloadSize, block.Data.Length - offset);
                var payload = new byte[2 + count];
                payload[0] = 0x36;
                payload[1] = (byte)(blockCounter & 0xFF);
                Buffer.BlockCopy(block.Data, offset, payload, 2, count);

                var response = await _udsClient.SendRawAsync(
                    payload,
                    ParseAddressing(step.AddressingMode),
                    requestTiming,
                    cancellationToken).ConfigureAwait(false);
                response.EnsurePositive();

                blockCounter = (blockCounter + 1) & 0xFF;
                var percentWithinImage = (int)Math.Round((completedBytes + offset + count) * 100.0 / image.Length);
                reportProgress?.Invoke(percentWithinImage, $"{kind} download {percentWithinImage}%");
            }

            var exitResponse = await _udsClient.SendRawAsync(
                [0x37],
                ParseAddressing(step.AddressingMode),
                requestTiming,
                cancellationToken).ConfigureAwait(false);
            exitResponse.EnsurePositive();
            log($"{kind} block downloaded: address=0x{block.Address:X8}, length={block.Data.Length}");
            completedBytes += block.Data.Length;
        }
    }

    private async Task<int?> RequestDownloadAsync(
        uint address,
        int length,
        FlashStepConfig step,
        UdsTimingOptions timing,
        CancellationToken cancellationToken)
    {
        var request = new byte[11];
        request[0] = 0x34;
        request[1] = 0x00;
        request[2] = 0x44;
        WriteUInt32BigEndian(request.AsSpan(3, 4), address);
        WriteUInt32BigEndian(request.AsSpan(7, 4), (uint)length);

        var response = await _udsClient.SendRawAsync(
            request,
            ParseAddressing(step.AddressingMode),
            timing,
            cancellationToken).ConfigureAwait(false);
        response.EnsurePositive();
        return ParseMaximumBlockLength(response.Payload);
    }

    private static int ResolveTransferDataSize(FlashStepConfig step, int? ecuMaximumBlockLength)
    {
        var configuredSize = HexUtil.ParseInt(step.BlockSize, 0xF0);
        if (configuredSize <= 0)
        {
            throw new InvalidOperationException("TransferData block size must be greater than zero.");
        }

        const int transferDataOverhead = 2;
        const int isoTpMaximumPayload = 4095;
        var maximumDataSize = isoTpMaximumPayload - transferDataOverhead;
        if (ecuMaximumBlockLength is > transferDataOverhead)
        {
            maximumDataSize = Math.Min(maximumDataSize, ecuMaximumBlockLength.Value - transferDataOverhead);
        }

        return Math.Min(configuredSize, maximumDataSize);
    }

    private static int? ParseMaximumBlockLength(byte[] responsePayload)
    {
        if (responsePayload.Length < 3 || responsePayload[0] != 0x74)
        {
            return null;
        }

        var lengthByteCount = responsePayload[1] >> 4;
        if (lengthByteCount <= 0 || lengthByteCount > 4 || responsePayload.Length < 2 + lengthByteCount)
        {
            return null;
        }

        uint value = 0;
        for (var index = 0; index < lengthByteCount; index++)
        {
            value = (value << 8) | responsePayload[2 + index];
        }

        return value is > 0 and <= int.MaxValue ? (int)value : null;
    }

    private static UdsTimingOptions ResolveTiming(FlashStepConfig step, UdsTimingOptions? timing)
    {
        return new UdsTimingOptions
        {
            P2ClientMs = ParsePositiveMilliseconds(step.TimeoutMs, timing?.P2ClientMs ?? 1500),
            P2StarClientMs = ParsePositiveMilliseconds(step.PendingTimeoutMs, timing?.P2StarClientMs ?? 30_000),
            S3ClientMs = timing?.S3ClientMs ?? 0,
            PendingOverallTimeoutMs = timing?.PendingOverallTimeoutMs ?? 30_000
        }.Validate();
    }

    private static int ParsePositiveMilliseconds(string? value, int fallbackMs)
    {
        var milliseconds = HexUtil.ParseInt(value, fallbackMs);
        return milliseconds > 0
            ? milliseconds
            : throw new InvalidOperationException("UDS timeout must be greater than zero.");
    }

    private static void WriteUInt32BigEndian(Span<byte> span, uint value)
    {
        span[0] = (byte)((value >> 24) & 0xFF);
        span[1] = (byte)((value >> 16) & 0xFF);
        span[2] = (byte)((value >> 8) & 0xFF);
        span[3] = (byte)(value & 0xFF);
    }

    private static UdsAddressing ParseAddressing(string? value)
    {
        return string.Equals(value, "functional", StringComparison.OrdinalIgnoreCase)
            ? UdsAddressing.Functional
            : UdsAddressing.Physical;
    }
}
