using System.Text.Json;
using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.Infrastructure.Configuration;

public sealed class JsonBootConfigRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly AppConfigurationPaths _paths;

    public JsonBootConfigRepository(AppConfigurationPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<string> ListConfigFiles()
    {
        if (!Directory.Exists(_paths.BootConfigDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_paths.BootConfigDirectory, "*.json")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public BootConfig Load(string fileName)
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(_paths.BootConfigDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("BOOT config file not found.", path);
        }

        var config = JsonSerializer.Deserialize<BootConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Invalid BOOT config: {path}");
        config.SourcePath = path;

        if (string.IsNullOrWhiteSpace(config.Name))
        {
            config.Name = Path.GetFileNameWithoutExtension(path);
        }

        return config;
    }

    public void Save(string fileName, BootConfig config)
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(_paths.BootConfigDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        config.SourcePath = path;
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }
}
