using DiagnosticFlashTool.Core.Can;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed class CanFrameRow
{
    public CanFrameRow(string direction, CanFrame frame)
    {
        Timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Direction = direction;
        Channel = frame.Channel;
        IdText = $"0x{frame.Id:X8}";
        FormatText = frame.IsExtended ? "扩展帧" : "标准帧";
        ProtocolText = frame.IsCanFd ? "CAN FD" : "CAN";
        BrsText = frame.IsCanFd ? frame.BitRateSwitch ? "是" : "否" : "—";
        EsiText = frame.IsCanFd ? frame.ErrorStateIndicator ? "是" : "否" : "—";
        Dlc = frame.Dlc;
        DataText = HexUtil.ToHex(frame.Data);
    }

    public string Timestamp { get; }
    public string Direction { get; }
    public uint Channel { get; }
    public string IdText { get; }
    public string FormatText { get; }
    public string ProtocolText { get; }
    public string BrsText { get; }
    public string EsiText { get; }
    public int Dlc { get; }
    public string DataText { get; }
}
