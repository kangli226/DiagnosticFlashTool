namespace DiagnosticFlashTool.Infrastructure.Configuration;

public sealed class AppConfigurationPaths
{
    public string RootDirectory { get; init; } = AppContext.BaseDirectory;

    public string ConfigDirectory => Path.Combine(RootDirectory, "resources", "configs");
    public string ProjectConfigPath => Path.Combine(ConfigDirectory, "projects.json");
    public string BootConfigDirectory => Path.Combine(ConfigDirectory, "BOOT");
    public string NativeDirectory => Path.Combine(RootDirectory, "resources", "native");
}
