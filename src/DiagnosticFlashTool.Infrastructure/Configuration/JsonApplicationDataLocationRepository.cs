using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiagnosticFlashTool.Infrastructure.Configuration;

/// <summary>
/// 在固定的用户目录中保存数据根目录入口，使设置文件随数据目录切换后仍可在启动时定位。
/// </summary>
public sealed class JsonApplicationDataLocationRepository(AppConfigurationPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string? Load()
    {
        if (!File.Exists(paths.ApplicationDataLocationFilePath))
        {
            return null;
        }

        try
        {
            var location = JsonSerializer.Deserialize<ApplicationDataLocation>(
                File.ReadAllText(paths.ApplicationDataLocationFilePath), JsonOptions);
            return string.IsNullOrWhiteSpace(location?.ApplicationDataDirectory)
                || !Path.IsPathFullyQualified(location.ApplicationDataDirectory)
                    ? null
                    : Path.GetFullPath(location.ApplicationDataDirectory);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void Save(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("应用数据目录必须使用绝对路径。", nameof(directory));
        }

        var location = new ApplicationDataLocation { ApplicationDataDirectory = Path.GetFullPath(directory) };
        Directory.CreateDirectory(paths.DefaultApplicationDataDirectory);
        var temporaryPath = paths.ApplicationDataLocationFilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(location, JsonOptions));
            File.Move(temporaryPath, paths.ApplicationDataLocationFilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record ApplicationDataLocation
    {
        [JsonPropertyName("applicationDataDirectory")]
        public string? ApplicationDataDirectory { get; init; }
    }
}
