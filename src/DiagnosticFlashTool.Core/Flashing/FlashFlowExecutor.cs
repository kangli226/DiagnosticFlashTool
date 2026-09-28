using DiagnosticFlashTool.Core.Algorithms;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Core.Flashing;

public sealed class FlashFlowExecutor : IFlashFlowExecutor
{
    /// <summary>
    /// 尽力清理时使用的短超时。清理操作不能沿用步骤超时，
    /// 否则 ECU 无响应时可能导致关闭过程停滞 30 秒。
    /// </summary>
    private static readonly UdsTimingOptions CleanupTiming = new UdsTimingOptions
    {
        P2ClientMs = 1000,
        P2StarClientMs = 1000,
        S3ServerTimeoutMs = 0,
        TesterPresentIntervalMs = 0,
        PendingOverallTimeoutMs = 2000
    }.Validate();

    /// <summary>未传输固件字节的步骤所使用的权重。</summary>
    private const double NominalUdsStepWeight = 1.0;

    /// <summary>为非下载步骤预留的进度占比。</summary>
    private const double NonDownloadProgressShare = 0.2;

    private readonly UdsClient _udsClient;
    private readonly SeedKeyAlgorithmRegistry _seedKeyAlgorithms;
    private readonly UdsFirmwareDownloader _firmwareDownloader;
    private readonly SemaphoreSlim _executionGate = new(1, 1);

    public FlashFlowExecutor(UdsClient udsClient, SeedKeyAlgorithmRegistry? seedKeyAlgorithms = null)
    {
        _udsClient = udsClient ?? throw new ArgumentNullException(nameof(udsClient));
        _seedKeyAlgorithms = seedKeyAlgorithms ?? new SeedKeyAlgorithmRegistry();
        _firmwareDownloader = new UdsFirmwareDownloader(_udsClient, CleanupTiming);
    }

    public async Task<FlashResult> ExecuteAsync(
        FlashExecutionRequest request,
        IProgress<FlashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteCoreAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task<FlashResult> ExecuteCoreAsync(
        FlashExecutionRequest request,
        IProgress<FlashProgress>? progress,
        CancellationToken cancellationToken)
    {
        var context = new FlashExecutionContext();

        try
        {
            ValidateRequest(request);
            var timing = ValidateTiming(request.Timing) ?? new UdsTimingOptions().Validate();
            context.ConfigureTiming(timing);

            // 拒绝执行可能静默跳过操作或中途失败的流程。对于未执行任何下载，
            // 或声明了 CRC 却未进行校验的流程，报告“刷写完成”比拒绝启动更危险。
            var issues = FlashFlowValidator.Validate(request.BootConfig, _seedKeyAlgorithms);
            var errors = issues.Where(issue => issue.Kind == FlashFlowIssueKind.Error).ToList();
            if (errors.Count > 0)
            {
                throw new FlashExecutionException(
                    FlashFailureKind.Validation,
                    "BOOT 流程校验未通过：" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors.Select(issue => "  - " + issue)));
            }

            foreach (var warning in issues.Where(issue => issue.Kind == FlashFlowIssueKind.Warning))
            {
                context.WriteLog($"警告：{warning}");
            }

            context.WriteLog($"开始刷写：BOOT={request.BootConfig.Name}");

            var weights = ComputeStepWeights(request.BootConfig, request.FirmwareSet);
            var totalWeight = weights.Sum();
            var completedWeight = 0.0;

            // 流程步骤必须严格串行。只有当前步骤完整执行且未抛出异常，
            // 才会进入下一次循环；不得在此处创建后台任务或并行等待多个步骤。
            for (var index = 0; index < request.BootConfig.Flow.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = request.BootConfig.Flow[index];
                var stepWeight = weights[index];
                var stepBaseWeight = completedWeight;

                void ReportStepProgress(int percentWithinStep, string message)
                {
                    var normalized = Math.Clamp(percentWithinStep, 0, 100) / 100.0;
                    var completed = stepBaseWeight + (stepWeight * normalized);
                    var overall = totalWeight > 0 ? completed / totalWeight : 0;
                    progress?.Report(new FlashProgress(
                        Math.Clamp((int)Math.Round(overall * 100), 0, 100),
                        message));
                }

                ReportStepProgress(0, $"步骤 {step.Id}：{step.Name}");
                context.WriteLog($"步骤 {step.Id}：{step.Name}");

                if (!FlashStepTypes.TryParse(step.StepType, out var kind))
                {
                    throw new FlashExecutionException(
                        FlashFailureKind.Validation,
                        $"步骤 {step.Id} 包含未知的步骤类型：{step.StepType}");
                }

                switch (kind)
                {
                    case FlashStepKind.DownloadDriver:
                        await DownloadDriverAsync(
                            request.FirmwareSet.Driver,
                            step,
                            context,
                            ReportStepProgress,
                            cancellationToken).ConfigureAwait(false);
                        break;

                    case FlashStepKind.DownloadApplication:
                        await DownloadApplicationsAsync(
                            request.FirmwareSet.GetApplicationImages(),
                            step,
                            context,
                            ReportStepProgress,
                            cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        await ExecuteUdsStepAsync(
                            step,
                            context,
                            cancellationToken).ConfigureAwait(false);
                        break;
                }

                ReportStepProgress(100, $"步骤 {step.Id} 已完成：{step.Name}");
                completedWeight += stepWeight;
                context.WriteLog($"步骤 {step.Id} 已完成：{step.Name}");
            }

            progress?.Report(new FlashProgress(100, "刷写完成"));
            context.WriteLog("刷写完成。");
            return new FlashResult
            {
                Success = true,
                UserMessage = "刷写完成",
                LogMessages = [.. context.Logs]
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.WriteLog("刷写已取消。");
            await TryReturnToDefaultSessionAsync(context).ConfigureAwait(false);
            throw;
        }
        catch (FlashExecutionException ex)
        {
            return await CreateFailureResultAsync(context, ex, ex.FailureKind).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            return await CreateFailureResultAsync(context, ex, FlashFailureKind.Timeout).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return await CreateFailureResultAsync(context, ex, FlashFailureKind.Transport).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return await CreateFailureResultAsync(context, ex, FlashFailureKind.Protocol).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.WriteLog($"未处理异常：{ex.Message}");
            await TryReturnToDefaultSessionAsync(context).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ExecuteUdsStepAsync(
        FlashStepConfig step,
        FlashExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!HexUtil.TryParseByte(step.Service, out var serviceId))
        {
            throw new FlashExecutionException(
                FlashFailureKind.Configuration,
                $"步骤 {step.Id} 没有可解析的 UDS 服务号。");
        }

        var parameters = new List<byte>();
        if (HexUtil.TryParseByte(step.SubService, out var subService))
        {
            parameters.Add(subService);
        }

        try
        {
            parameters.AddRange(step.Extend.SelectMany(HexUtil.ParseBytes));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new FlashExecutionException(
                FlashFailureKind.Configuration,
                $"步骤 {step.Id} 的附加参数不是有效的十六进制字节。",
                ex);
        }

        if (serviceId == 0x27
            && HexUtil.TryParseByte(step.SubService, out subService)
            && subService % 2 == 0
            && !string.IsNullOrWhiteSpace(step.SecurityAlgorithm))
        {
            if (context.LastSeed.Length == 0)
            {
                throw new FlashExecutionException(
                    FlashFailureKind.SecurityAccess,
                    "尚未收到 Seed，无法计算安全访问 Key。");
            }

            var algorithm = _seedKeyAlgorithms.Resolve(step.SecurityAlgorithm);
            byte[] key;
            try
            {
                key = await algorithm
                    .ComputeKeyAsync(context.LastSeed, step.AlgorithmParams.ToList(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FlashExecutionException(
                    FlashFailureKind.SecurityAccess,
                    $"安全访问算法 {algorithm.Name} 计算 Key 失败：{ex.Message}",
                    ex);
            }

            parameters.AddRange(key);

            // Seed 只能使用一次。使用后立即清除，避免流程为同一个 Seed 声明两个
            // 发送 Key 步骤时意外重放。
            context.LastSeed = [];
            context.WriteLog($"安全访问算法 {algorithm.Name} 已生成 {key.Length} 字节 Key。");
        }

        var response = await _udsClient.SendAsync(
            serviceId,
            parameters,
            FlashStepUdsSettings.ParseAddressing(step.AddressingMode),
            context.EffectiveTiming,
            cancellationToken).ConfigureAwait(false);

        FlashStepUdsSettings.EnsurePositive(response);
        context.WriteLog($"接收：{response}");

        if (serviceId == 0x27
            && response.Payload.Length > 2
            && response.Payload[0] == 0x67
            && response.Payload[1] % 2 == 1)
        {
            context.LastSeed = response.Payload.Skip(2).ToArray();
        }

        UpdateSessionStateAndTiming(serviceId, step.SubService, response, context);
    }

    private async Task DownloadDriverAsync(
        FirmwareImage? image,
        FlashStepConfig step,
        FlashExecutionContext context,
        Action<int, string> reportProgress,
        CancellationToken cancellationToken)
    {
        var firmware = RequireFirmware(image, FirmwareImageKind.Driver);
        await _firmwareDownloader.DownloadAsync(
            firmware,
            step,
            FirmwareImageKind.Driver,
            item =>
            {
                var percent = CalculatePercent(item.TransferredBytes, item.TotalBytes);
                reportProgress(percent, item.Message);
            },
            context,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadApplicationsAsync(
        IReadOnlyList<FirmwareImage> images,
        FlashStepConfig step,
        FlashExecutionContext context,
        Action<int, string> reportProgress,
        CancellationToken cancellationToken)
    {
        if (images.Count == 0)
        {
            _ = RequireFirmware(null, FirmwareImageKind.Application);
        }

        foreach (var image in images)
        {
            _ = RequireFirmware(image, FirmwareImageKind.Application);
        }

        var totalBytes = images.Sum(image => (long)image.Length);
        long completedBytes = 0;

        foreach (var image in images)
        {
            var imageBaseBytes = completedBytes;
            await _firmwareDownloader.DownloadAsync(
                image,
                step,
                FirmwareImageKind.Application,
                item =>
                {
                    var percent = CalculatePercent(imageBaseBytes + item.TransferredBytes, totalBytes);
                    reportProgress(percent, $"应用固件下载 {percent}%");
                },
                context,
                cancellationToken).ConfigureAwait(false);
            completedBytes += image.Length;
        }
    }

    /// <summary>
    /// 刷写失败或取消后将 ECU 恢复到默认会话，避免其停留在编程会话中。
    /// 此操作仅作尽力清理，绝不能掩盖原始故障。
    /// </summary>
    private async Task TryReturnToDefaultSessionAsync(FlashExecutionContext context)
    {
        if (!context.NonDefaultSessionActive)
        {
            return;
        }

        context.NonDefaultSessionActive = false;
        try
        {
            var response = await _udsClient
                .SendAsync(0x10, [0x01], UdsAddressing.Physical, CleanupTiming, CancellationToken.None)
                .ConfigureAwait(false);
            FlashStepUdsSettings.EnsurePositive(response);
            context.WriteLog("已返回默认会话（0x10 0x01）。");
        }
        catch (Exception ex)
        {
            context.WriteLog($"返回默认会话失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 按传输字节数而非步骤数计算进度权重。下载步骤可能传输数百 KB，
    /// 而会话或安全访问步骤仅传输几个字节；若平均分配进度，进度条会在
    /// 几乎整个刷写期间停滞不前。
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
            _ = FlashStepTypes.TryParse(step.StepType, out var kind);
            var weight = kind switch
            {
                FlashStepKind.DownloadDriver => Math.Max(1, firmwareSet.Driver?.Length ?? 0),
                FlashStepKind.DownloadApplication => Math.Max(1, applicationBytes),
                _ => 0
            };

            if (kind == FlashStepKind.Uds)
            {
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
            // 没有步骤传输字节时，步骤数是唯一合理的进度衡量方式。
            Array.Fill(weights, NominalUdsStepWeight);
            return weights;
        }

        if (udsSteps == 0)
        {
            return weights;
        }

        // 为非下载步骤预留固定比例并平均分配，使进度条在会话、安全访问和例程步骤中
        // 仍能继续推进。
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

    private async Task<FlashResult> CreateFailureResultAsync(
        FlashExecutionContext context,
        Exception exception,
        FlashFailureKind failureKind)
    {
        context.WriteLog($"错误：{exception.Message}");
        await TryReturnToDefaultSessionAsync(context).ConfigureAwait(false);
        return new FlashResult
        {
            Success = false,
            UserMessage = exception.Message,
            FailureKind = failureKind,
            LogMessages = [.. context.Logs]
        };
    }

    private static void ValidateRequest(FlashExecutionRequest request)
    {
        if (request.BootConfig is null)
        {
            throw new FlashExecutionException(FlashFailureKind.Configuration, "BOOT 流程配置不能为空。");
        }

        if (request.FirmwareSet is null)
        {
            throw new FlashExecutionException(FlashFailureKind.Configuration, "固件集合不能为空。");
        }

    }

    private static UdsTimingOptions? ValidateTiming(UdsTimingOptions? timing)
    {
        try
        {
            return timing?.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new FlashExecutionException(
                FlashFailureKind.Configuration,
                $"UDS 时序配置无效：{ex.Message}",
                ex);
        }
    }

    private static FirmwareImage RequireFirmware(FirmwareImage? image, FirmwareImageKind kind)
    {
        if (image is not null && image.Length > 0)
        {
            return image;
        }

        var kindName = kind == FirmwareImageKind.Driver ? "驱动" : "应用";
        throw new FlashExecutionException(
            FlashFailureKind.Configuration,
            $"{kindName}固件下载步骤没有可用固件：请在“固件刷写”页选择固件文件，或从 BOOT 流程中移除该步骤。");
    }

    private static int CalculatePercent(long completedBytes, long totalBytes)
    {
        return totalBytes <= 0
            ? 0
            : Math.Clamp((int)Math.Round(completedBytes * 100.0 / totalBytes), 0, 100);
    }

    private static void UpdateSessionStateAndTiming(
        byte serviceId,
        string? subService,
        UdsResponse response,
        FlashExecutionContext context)
    {
        if (serviceId == 0x10 && HexUtil.TryParseByte(subService, out var sessionType))
        {
            context.NonDefaultSessionActive = (sessionType & 0x7F) != 0x01;
            context.ResetEffectiveTiming();

            if (UdsSessionTiming.TryParse(response, out var sessionTiming))
            {
                context.EffectiveTiming = sessionTiming.CreateClientTiming(context.ConfiguredTiming);
                context.WriteLog(
                    $"ECU 会话时序：P2ServerMax={sessionTiming.P2ServerMaxMs}ms，" +
                    $"P2*ServerMax={sessionTiming.P2StarServerMaxMs}ms；" +
                    $"后续使用 P2Client={context.EffectiveTiming.P2ClientMs}ms，" +
                    $"P2*Client={context.EffectiveTiming.P2StarClientMs}ms。");
            }
            else
            {
                context.WriteLog(
                    $"警告：0x50 应答未包含有效会话时序，后续沿用配置值 " +
                    $"P2Client={context.ConfiguredTiming.P2ClientMs}ms，" +
                    $"P2*Client={context.ConfiguredTiming.P2StarClientMs}ms。");
            }

            return;
        }

        if (serviceId == 0x11)
        {
            context.NonDefaultSessionActive = false;
            context.ResetEffectiveTiming();
        }
    }
}
