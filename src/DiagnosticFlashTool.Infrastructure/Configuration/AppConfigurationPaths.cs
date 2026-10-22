namespace DiagnosticFlashTool.Infrastructure.Configuration;

public sealed class AppConfigurationPaths
{
    public string RootDirectory { get; init; } = AppContext.BaseDirectory;

    public string DefaultApplicationDataDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DOTRUST",
        "AP_BTMSV02");
    public string ApplicationDataDirectory => ConfigDirectoryOverride ?? DefaultApplicationDataDirectory;
    public string ApplicationDataLocationFilePath => Path.Combine(DefaultApplicationDataDirectory, "data-location.json");
    public string SettingsFilePath => Path.Combine(ApplicationDataDirectory, "settings.json");
    public string DefaultConfigDirectory => DefaultApplicationDataDirectory;
    public string DefaultProjectConfigPath => Path.Combine(DefaultConfigDirectory, "projects.json");
    public string DefaultBootConfigDirectory => Path.Combine(DefaultConfigDirectory, "BOOT");
    public string DefaultScriptDirectory => Path.Combine(DefaultConfigDirectory, "Scripts");

    public string BuiltInConfigDirectory => Path.Combine(RootDirectory, "resources", "configs");
    public string BuiltInProjectConfigPath => Path.Combine(BuiltInConfigDirectory, "projects.json");
    public string BuiltInBootConfigDirectory => Path.Combine(BuiltInConfigDirectory, "BOOT");
    public string BuiltInScriptDirectory => Path.Combine(BuiltInConfigDirectory, "Scripts");

    public string? ConfigDirectoryOverride { get; set; }
    public string? ProjectConfigPathOverride { get; set; }
    public string? BootConfigDirectoryOverride { get; set; }

    public string ConfigDirectory => ApplicationDataDirectory;
    public string ProjectConfigPath => ProjectConfigPathOverride ?? Path.Combine(ConfigDirectory, "projects.json");
    public string BootConfigDirectory => BootConfigDirectoryOverride ?? Path.Combine(ConfigDirectory, "BOOT");
    public string ScriptDirectory => Path.Combine(ConfigDirectory, "Scripts");
    public string NativeDirectory => Path.Combine(RootDirectory, "resources", "native");
}
