namespace DiagnosticFlashTool.Infrastructure.Configuration;

public sealed class AppConfigurationPaths
{
    public string RootDirectory { get; init; } = AppContext.BaseDirectory;

    public string DefaultConfigDirectory => Path.Combine(RootDirectory, "resources", "configs");
    public string DefaultProjectConfigPath => Path.Combine(DefaultConfigDirectory, "projects.json");
    public string DefaultBootConfigDirectory => Path.Combine(DefaultConfigDirectory, "BOOT");
    public string DefaultFormulaDatabaseDirectory => Path.Combine(DefaultConfigDirectory, "FormulaDatabase");

    public string? ConfigDirectoryOverride { get; set; }
    public string? ProjectConfigPathOverride { get; set; }
    public string? BootConfigDirectoryOverride { get; set; }
    public string? FormulaDatabaseDirectoryOverride { get; set; }

    public string ConfigDirectory => ConfigDirectoryOverride ?? DefaultConfigDirectory;
    public string ProjectConfigPath => ProjectConfigPathOverride ?? Path.Combine(ConfigDirectory, "projects.json");
    public string BootConfigDirectory => BootConfigDirectoryOverride ?? Path.Combine(ConfigDirectory, "BOOT");
    public string FormulaDatabaseDirectory => FormulaDatabaseDirectoryOverride ?? DefaultFormulaDatabaseDirectory;
    public string NativeDirectory => Path.Combine(RootDirectory, "resources", "native");
}
