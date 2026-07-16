using System.Text.Json;
using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.Infrastructure.Configuration;

public sealed class JsonProjectConfigRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly AppConfigurationPaths _paths;

    public JsonProjectConfigRepository(AppConfigurationPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<ProjectConfigEntry> LoadAll()
    {
        if (!File.Exists(_paths.ProjectConfigPath))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ProjectConfigEntry>>(File.ReadAllText(_paths.ProjectConfigPath), JsonOptions) ?? [];
    }

    public void SaveAll(IEnumerable<ProjectConfigEntry> projects)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.ProjectConfigPath)!);
        File.WriteAllText(_paths.ProjectConfigPath, JsonSerializer.Serialize(projects, JsonOptions));
    }
}
