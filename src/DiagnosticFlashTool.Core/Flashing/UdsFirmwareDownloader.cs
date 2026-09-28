using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Core.Flashing;

internal sealed record FirmwareTransferProgress(long TransferredBytes, long TotalBytes, string Message);

/// <summary>执行 UDS 0x34、0x36、0x37 固件传输。</summary>
internal sealed class UdsFirmwareDownloader(UdsClient udsClient, UdsTimingOptions cleanupTiming)
{
    public async Task DownloadAsync(
        FirmwareImage image,
        FlashStepConfig step,
        FirmwareImageKind kind,
        Action<FirmwareTransferProgress>? reportProgress,
        FlashExecutionContext context,
        CancellationToken cancellationToken)
    {
        var addressing = FlashStepUdsSettings.ParseAddressing(step.AddressingMode);
        var requestTiming = context.EffectiveTiming;
        long completedBytes = 0;

        foreach (var block in image.Blocks)
        {
            var ecuMaximumBlockLength = await RequestDownloadAsync(
                block.Address,
                block.Data.Length,
                addressing,
                requestTiming,
                cancellationToken).ConfigureAwait(false);

            // 从此处开始，ECU 将等待 RequestTransferExit。finally 块确保传输取消或失败时
            // 仍会发送 0x37，避免 ECU 一直等待后续 TransferData。
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

                    var response = await udsClient.SendRawAsync(
                        payload,
                        addressing,
                        requestTiming,
                        cancellationToken).ConfigureAwait(false);
                    FlashStepUdsSettings.EnsurePositive(response);

                    blockCounter = (blockCounter + 1) & 0xFF;
                    var transferredBytes = completedBytes + offset + count;
                    var percent = (int)Math.Round(transferredBytes * 100.0 / image.Length);
                    reportProgress?.Invoke(new FirmwareTransferProgress(
                        transferredBytes,
                        image.Length,
                        $"{GetKindName(kind)}下载 {percent}%"));
                }

                var exitResponse = await udsClient.SendRawAsync(
                    [0x37],
                    addressing,
                    requestTiming,
                    cancellationToken).ConfigureAwait(false);
                FlashStepUdsSettings.EnsurePositive(exitResponse);
                transferOpen = false;
            }
            finally
            {
                if (transferOpen)
                {
                    await TryRequestTransferExitAsync(addressing, kind, block, context).ConfigureAwait(false);
                }
            }

            context.WriteLog($"{GetKindName(kind)}块下载完成：地址=0x{block.Address:X8}，长度={block.Data.Length} 字节");
            completedBytes += block.Data.Length;
        }
    }

    private async Task<int?> RequestDownloadAsync(
        uint address,
        int length,
        UdsAddressing addressing,
        UdsTimingOptions timing,
        CancellationToken cancellationToken)
    {
        var request = new byte[11];
        request[0] = 0x34;
        request[1] = 0x00;
        request[2] = 0x44;
        WriteUInt32BigEndian(request.AsSpan(3, 4), address);
        WriteUInt32BigEndian(request.AsSpan(7, 4), (uint)length);

        var response = await udsClient.SendRawAsync(
            request,
            addressing,
            timing,
            cancellationToken).ConfigureAwait(false);
        FlashStepUdsSettings.EnsurePositive(response);
        return ParseMaximumBlockLength(response.Payload);
    }

    /// <summary>
    /// 下载中止后尽力发送 RequestTransferExit。使用独立的取消令牌和超时，
    /// 以确保取消刷写时仍可执行，并且不会用清理失败覆盖原始故障。
    /// </summary>
    private async Task TryRequestTransferExitAsync(
        UdsAddressing addressing,
        FirmwareImageKind kind,
        FirmwareBlock block,
        FlashExecutionContext context)
    {
        try
        {
            var response = await udsClient
                .SendRawAsync([0x37], addressing, cleanupTiming, CancellationToken.None)
                .ConfigureAwait(false);
            FlashStepUdsSettings.EnsurePositive(response);
            context.WriteLog($"{GetKindName(kind)}块 0x{block.Address:X8} 已中止，已补发 0x37 结束传输。");
        }
        catch (Exception ex)
        {
            context.WriteLog($"{GetKindName(kind)}块 0x{block.Address:X8} 传输收尾失败：{ex.Message}");
        }
    }

    private static int ResolveTransferDataSize(FlashStepConfig step, int? ecuMaximumBlockLength)
    {
        int configuredSize;
        try
        {
            configuredSize = HexUtil.ParseInt(step.BlockSize, 0xF0);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new FlashExecutionException(
                FlashFailureKind.Configuration,
                $"步骤 {step.Id} 的 TransferData 块大小无效：{step.BlockSize}",
                ex);
        }

        if (configuredSize <= 0)
        {
            throw new FlashExecutionException(
                FlashFailureKind.Configuration,
                $"步骤 {step.Id} 的 TransferData 块大小必须大于零。");
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

    private static void WriteUInt32BigEndian(Span<byte> span, uint value)
    {
        span[0] = (byte)((value >> 24) & 0xFF);
        span[1] = (byte)((value >> 16) & 0xFF);
        span[2] = (byte)((value >> 8) & 0xFF);
        span[3] = (byte)(value & 0xFF);
    }

    private static string GetKindName(FirmwareImageKind kind)
    {
        return kind == FirmwareImageKind.Driver ? "驱动固件" : "应用固件";
    }
}
