namespace DiagnosticFlashTool.Core.Diagnostics;

public sealed class DiagnosticTransportOptions
{
    public uint PhysicalRequestId { get; set; } = 0x18DA5535;
    public uint FunctionalRequestId { get; set; } = 0x18DA55FF;
    public uint ResponseId { get; set; } = 0x18DA3555;
    public uint Channel { get; set; }
    public bool ExtendedFrame { get; set; } = true;
    public int FlowControlBlockSize { get; set; } = 0x0A;
    public int FlowControlStMinMs { get; set; } = 10;
}
