using DiagnosticFlashTool.Core.Diagnostics;

namespace DiagnosticFlashTool.Core.Flashing;

/// <summary>仅在单次刷写期间存活的可变状态。</summary>
internal sealed class FlashExecutionContext
{
    private readonly Dictionary<string, ulong> _runtimeVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingCrcVerifications = new(StringComparer.OrdinalIgnoreCase);

    public UdsTimingOptions ConfiguredTiming { get; private set; } = new();

    public UdsTimingOptions EffectiveTiming { get; set; } = new();

    public byte[] LastSeed { get; set; } = [];

    public bool NonDefaultSessionActive { get; set; }

    public List<string> Logs { get; } = [];

    public IReadOnlyDictionary<string, ulong> RuntimeVariables => _runtimeVariables;

    public IReadOnlyCollection<string> PendingCrcVerifications => _pendingCrcVerifications;

    public void ConfigureTiming(UdsTimingOptions timing)
    {
        ConfiguredTiming = timing;
        EffectiveTiming = timing;
    }

    public void ResetEffectiveTiming()
    {
        EffectiveTiming = ConfiguredTiming;
    }

    public void SetFirmwareRuntimeValues(string prefix, uint startAddress, uint length, uint crc)
    {
        _runtimeVariables[$"{prefix}.start"] = startAddress;
        _runtimeVariables[$"{prefix}.length"] = length;
        _runtimeVariables[$"{prefix}.crc"] = crc;
        _pendingCrcVerifications.Add(prefix);
    }

    public void ConfirmCrcVariables(IEnumerable<string> referencedVariables)
    {
        foreach (var variable in referencedVariables)
        {
            if (variable.EndsWith(".crc", StringComparison.OrdinalIgnoreCase))
            {
                _pendingCrcVerifications.Remove(variable[..^4]);
            }
        }
    }

    public void WriteLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Logs.Add(line);
    }
}
