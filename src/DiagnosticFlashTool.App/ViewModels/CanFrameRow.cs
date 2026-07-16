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
        Dlc = frame.Dlc;
        DataText = HexUtil.ToHex(frame.Data);
    }

    public string Timestamp { get; }
    public string Direction { get; }
    public uint Channel { get; }
    public string IdText { get; }
    public int Dlc { get; }
    public string DataText { get; }
}
