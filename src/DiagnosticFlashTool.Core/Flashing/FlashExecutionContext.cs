using DiagnosticFlashTool.Core.Diagnostics;

namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>仅在单次刷写期间存活的可变状态。</summary>
internal sealed class FlashExecutionContext
{
    public UdsTimingOptions ConfiguredTiming { get; private set; } = new();

    public UdsTimingOptions EffectiveTiming { get; set; } = new();

    public byte[] LastSeed { get; set; } = [];

    public bool NonDefaultSessionActive { get; set; }

    public List<string> Logs { get; } = [];

    public void ConfigureTiming(UdsTimingOptions timing)
    {
        ConfiguredTiming = timing;
        EffectiveTiming = timing;
    }

    public void ResetEffectiveTiming()
    {
        EffectiveTiming = ConfiguredTiming;
    }

    public void WriteLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Logs.Add(line);
    }
}
