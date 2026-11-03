namespace DiagnosticFlashTool.Core.Can;

public enum BaudRateSelectionMode
{
    Auto,
    Fixed,
    Project
}

public static class BaudRateSelectionModeExtensions
{
    public static BaudRateSelectionMode Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "fixed" => BaudRateSelectionMode.Fixed,
            "project" => BaudRateSelectionMode.Project,
            _ => BaudRateSelectionMode.Auto
        };
    }

    public static string ToConfigValue(this BaudRateSelectionMode mode) => mode switch
    {
        BaudRateSelectionMode.Fixed => "fixed",
        BaudRateSelectionMode.Project => "project",
        _ => "auto"
    };
}
