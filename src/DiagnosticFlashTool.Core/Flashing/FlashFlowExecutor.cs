using DiagnosticFlashTool.Core.Algorithms;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Core.Flashing;

public sealed class FlashFlowExecutor
{
    /// <summary>
    /// Short timing used by best-effort cleanup calls. Cleanup must not inherit the
    /// step timing, otherwise a missing ECU could stall shutdown for 30 seconds.
    /// </summary>
    private static readonly UdsTimingOptions CleanupTiming = new UdsTimingOptions
    {
        P2ClientMs = 1000,
        P2StarClientMs = 1000,
        S3ClientMs = 0,
        PendingOverallTimeoutMs = 2000
    }.Validate();

    /// <summary>Weight given to a step that transfers no firmware bytes.</summary>
    private const double NominalUdsStepWeight = 1.0;

    /// <summary>Share of the progress bar reserved for non-download steps.</summary>
    private const double NonDownloadProgressShare = 0.2;

    private readonly UdsClient _udsClient;
    private readonly SeedKeyAlgorithmRegistry _seedKeyAlgorithms;
    private byte[] _lastSeed = [];
    private bool _nonDefaultSessionActive;

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
        _nonDefaultSessionActive = false;
        var logs = new List<string>();
        void WriteLog(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            logs.Add(line);
            log?.Invoke(line);
        }

        try
        {
            // Refuse a flow that would silently do nothing or fail halfway. Reporting
            // "flash completed" for a flow that never downloaded, or whose declared CRC
            // was never checked, is worse than refusing to start.
            var issues = FlashFlowValidator.Validate(bootConfig, _seedKeyAlgorithms);
            var errors = issues.Where(issue => issue.Kind == FlashFlowIssueKind.Error).ToList();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "BOOT 流程校验未通过：" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors.Select(issue => "  - " + issue)));
            }

            foreach (var warning in issues.Where(issue => issue.Kind == FlashFlowIssueKind.Warning))
            {
                WriteLog($"WARNING: {warning}");
            }

            WriteLog($"Start flash: project={project.ProjectName}, boot={bootConfig.Name}");

            var weights = ComputeStepWeights(bootConfig, firmwareSet);
            var totalWeight = weights.Sum();
            var completedWeight = 0.0;

            for (var index = 0; index < bootConfig.Flow.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = bootConfig.Flow[index];
                var stepWeight = weights[index];
                var stepBaseWeight = completedWeight;

                void ReportStepProgress(int percentWithinStep, string message)
                {
                    var normalized = Math.Clamp(percentWithinStep, 0, 100) / 100.0;
                    var completed = stepBaseWeight + (stepWeight * normalized);
                    var overall = totalWeight > 0 ? completed / totalWeight : 0;
                    progress?.Report(new FlashProgress(Math.Clamp((int)Math.Round(overall * 100), 0, 100), message));
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

                completedWeight += stepWeight;
                WriteLog($"Step {step.Id} completed: {step.Name}");
            }

            progress?.Report(new FlashProgress(100, "Flash completed"));
            WriteLog("Flash completed.");
            return new FlashResult { Success = true, UserMessage = "刷写完成", LogMessages = logs };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteLog("Flash canceled.");
            await TryReturnToDefaultSessionAsync(WriteLog).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            WriteLog($"ERROR: {ex.Message}");
            await TryReturnToDefaultSessionAsync(WriteLog).ConfigureAwait(false);
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
            if (_lastSeed.Length == 0)
            {
                throw new InvalidOperationException("Security key requested before a seed was received.");
            }

            var algorithm = _seedKeyAlgorithms.Resolve(step.SecurityAlgorithm);
            var key = await algorithm
                .ComputeKeyAsync(_lastSeed, step.AlgorithmParams.ToList(), cancellationToken)
                .ConfigureAwait(false);

            parameters.AddRange(key);

            // A seed is single-use. Dropping it prevents an accidental replay when a
            // flow declares two send-key steps for the same seed.
            _lastSeed = [];
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

        if (serviceId == 0x10 && HexUtil.TryParseByte(step.SubService, out var sessionType) && sessionType != 0x01)
        {
            // Anything other than the default session has to be undone if the flow fails,
            // otherwise the ECU is left inside the programming session.
            _nonDefaultSessionActive = true;
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
            // A download step with nothing to download is a configuration error.
            // Returning quietly here is exactly what let a flow report "刷写完成"
            // without having written a single byte.
            throw new InvalidOperationException(
                $"{kind} 下载步骤没有可用的固件：请在“固件刷写”页选择固件文件，或从 BOOT 流程中移除该步骤。");
        }

        var addressing = ParseAddressing(step.AddressingMode);
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

            // From here on the ECU expects a RequestTransferExit. The finally block
            // guarantees 0x37 is sent even when the transfer is cancelled or fails,
            // so the ECU is not left waiting for more TransferData.
            var transferOpen = true;
            try
            {
                var payloadSize = ResolveTransferDataSize(step, ecuMaximumBlockLength);
                var blockCounter = 1;
                for (var offset = 0; offset < block.Data.Length; offset += payloadSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = Math.Min(payloadSize, block.Data.Length - offset);
                    var payload = new byte[2 + count];
                    payload[0] = 0x36;
                    payload[1] = (byte)(blockCounter & 0xFF);
                    Buffer.BlockCopy(block.Data, offset, payload, 2, count);

                    var response = await _udsClient.SendRawAsync(
                        payload,
                        addressing,
                        requestTiming,
                        cancellationToken).ConfigureAwait(false);
                    response.EnsurePositive();

                    blockCounter = (blockCounter + 1) & 0xFF;
                    var percentWithinImage = (int)Math.Round((completedBytes + offset + count) * 100.0 / image.Length);
                    reportProgress?.Invoke(percentWithinImage, $"{kind} download {percentWithinImage}%");
                }

                var exitResponse = await _udsClient.SendRawAsync(
                    [0x37],
                    addressing,
                    requestTiming,
                    cancellationToken).ConfigureAwait(false);
                exitResponse.EnsurePositive();
                transferOpen = false;
            }
            finally
            {
                if (transferOpen)
                {
                    await TryRequestTransferExitAsync(addressing, kind, block, log).ConfigureAwait(false);
                }
            }

            log($"{kind} block downloaded: address=0x{block.Address:X8}, length={block.Data.Length}");
            completedBytes += block.Data.Length;
        }
    }

    /// <summary>
    /// Best-effort RequestTransferExit after an aborted download. Runs on its own token
    /// and timing so it still executes while the flash is being cancelled, and it never
    /// replaces the original failure with a cleanup failure.
    /// </summary>
    private async Task TryRequestTransferExitAsync(
        UdsAddressing addressing,
        FirmwareImageKind kind,
        FirmwareBlock block,
        Action<string> log)
    {
        try
        {
            var response = await _udsClient
                .SendRawAsync([0x37], addressing, CleanupTiming, CancellationToken.None)
                .ConfigureAwait(false);
            response.EnsurePositive();
            log($"{kind} block 0x{block.Address:X8} 已中止，已补发 0x37 结束传输。");
        }
        catch (Exception ex)
        {
            log($"{kind} block 0x{block.Address:X8} 传输收尾失败：{ex.Message}");
        }
    }

    /// <summary>
    /// Returns the ECU to the default session after a failed or cancelled flash so it is
    /// not left inside the programming session. Best-effort only: this must never mask
    /// the original failure.
    /// </summary>
    private async Task TryReturnToDefaultSessionAsync(Action<string> log)
    {
        if (!_nonDefaultSessionActive)
        {
            return;
        }

        _nonDefaultSessionActive = false;
        try
        {
            var response = await _udsClient
                .SendAsync(0x10, [0x01], UdsAddressing.Physical, CleanupTiming, CancellationToken.None)
                .ConfigureAwait(false);
            response.EnsurePositive();
            log("已返回默认会话（0x10 0x01）。");
        }
        catch (Exception ex)
        {
            log($"返回默认会话失败：{ex.Message}");
        }
    }

    /// <summary>
    /// Weights progress by transferred bytes instead of by step count. A download step
    /// moves hundreds of kilobytes while a session or security step moves a few bytes, so
    /// dividing the bar equally makes it stall for almost the whole flash.
    /// </summary>
    private static double[] ComputeStepWeights(BootConfig bootConfig, FirmwareSet firmwareSet)
    {
        var weights = new double[bootConfig.Flow.Count];
        var applicationBytes = firmwareSet.GetApplicationImages().Sum(image => (double)image.Length);
        var downloadWeight = 0.0;
        var downloadSteps = 0;
        var udsSteps = 0;

        for (var index = 0; index < bootConfig.Flow.Count; index++)
        {
            var step = bootConfig.Flow[index];
            double weight;

            if (string.Equals(step.StepType, "DownloadDriver", StringComparison.OrdinalIgnoreCase))
            {
                weight = Math.Max(1, firmwareSet.Driver?.Length ?? 0);
            }
            else if (string.Equals(step.StepType, "DownloadApplication", StringComparison.OrdinalIgnoreCase))
            {
                weight = Math.Max(1, applicationBytes);
            }
            else
            {
                weight = 0;
                udsSteps++;
            }

            if (weight > 0)
            {
                downloadWeight += weight;
                downloadSteps++;
            }

            weights[index] = weight;
        }

        if (downloadSteps == 0)
        {
            // Nothing transfers bytes, so step count is the only sensible measure.
            Array.Fill(weights, NominalUdsStepWeight);
            return weights;
        }

        if (udsSteps == 0)
        {
            return weights;
        }

        // Reserve a fixed share for the non-download steps and split it equally, so the
        // bar still advances during session, security and routine steps.
        var total = downloadWeight / (1 - NonDownloadProgressShare);
        var udsWeight = (total - downloadWeight) / udsSteps;

        for (var index = 0; index < weights.Length; index++)
        {
            if (weights[index] <= 0)
            {
                weights[index] = udsWeight;
            }
        }

        return weights;
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
