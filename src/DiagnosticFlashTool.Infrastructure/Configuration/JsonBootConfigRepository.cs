using System.Text.Json;
using System.Text.Json.Nodes;
using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.Infrastructure.Configuration;

/// <summary>
/// 在 JSON 文件与 <see cref="BootConfig"/> 模型之间加载和保存 BOOT 刷写流程配置。
/// </summary>
/// <remarks>
/// 相对文件名以 <see cref="AppConfigurationPaths.BootConfigDirectory"/> 为基准；
/// 绝对路径则直接使用。保存现有配置时仅替换流程数组。
/// </remarks>
public sealed class JsonBootConfigRepository
{
    /// <summary>
    /// BOOT 配置的统一 JSON 读写选项。读取时允许注释和尾随逗号，写入时使用缩进格式。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly AppConfigurationPaths _paths;

    /// <summary>
    /// 初始化 BOOT 配置仓储。
    /// </summary>
    /// <param name="paths">应用配置文件的路径定义。</param>
    public JsonBootConfigRepository(AppConfigurationPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// 获取 BOOT 配置目录下的所有 JSON 配置文件名。
    /// </summary>
    /// <returns>按名称排序的配置文件名；目录不存在时返回空列表。</returns>
    public IReadOnlyList<string> ListConfigFileNames()
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

    /// <summary>
    /// 从 JSON 文件加载一份 BOOT 刷写流程配置。
    /// </summary>
    /// <param name="fileName">配置文件名，或配置文件的绝对路径。</param>
    /// <returns>反序列化后的 BOOT 配置。</returns>
    /// <exception cref="FileNotFoundException">目标配置文件不存在。</exception>
    /// <exception cref="InvalidDataException">配置文件不能反序列化为有效对象。</exception>
    /// <exception cref="JsonException">配置文件包含无效 JSON。</exception>
    public BootConfig Load(string fileName)
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(_paths.BootConfigDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("BOOT config file not found.", path);
        }

        var config = JsonSerializer.Deserialize<BootConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Invalid BOOT config: {path}");

        // 旧配置可能未声明名称，使用文件名保证日志和界面始终有可识别的名称。
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            config.Name = Path.GetFileNameWithoutExtension(path);
        }

        return config;
    }

    /// <summary>
    /// 保存 BOOT 刷写流程配置。
    /// </summary>
    /// <param name="fileName">配置文件名，或配置文件的绝对路径。</param>
    /// <param name="config">要保存的完整 BOOT 配置。</param>
    /// <remarks>
    /// 目标文件已存在时只替换 <c>flow</c> 数组，保留 scripts、扩展字段及其他未编辑内容；
    /// 目标文件不存在时写入完整配置。
    /// </remarks>
    public void Save(string fileName, BootConfig config)
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(_paths.BootConfigDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
            return;
        }

        var root = ParseJsonObject(path);
        root["flow"] = JsonSerializer.SerializeToNode(config.Flow, JsonOptions);
        File.WriteAllText(path, root.ToJsonString(JsonOptions));
    }

    /// <summary>
    /// 以现有 BOOT JSON 为快照创建独立配置，并更新新配置的名称。
    /// </summary>
    /// <remarks>
    /// 使用 JSON DOM 复制，确保当前模型尚未声明的扩展字段也会保留；新文件与源文件不存在继承关系。
    /// </remarks>
    public void SaveSnapshot(string sourceFileName, string targetFileName, string configName)
    {
        SaveSnapshotJson(targetFileName, CreateSnapshotJson(sourceFileName, configName));
    }

    /// <summary>
    /// 读取源配置并生成可延迟写入的独立 JSON 快照。
    /// </summary>
    public string CreateSnapshotJson(string sourceFileName, string configName)
    {
        var sourcePath = Path.IsPathRooted(sourceFileName)
            ? sourceFileName
            : Path.Combine(_paths.BootConfigDirectory, sourceFileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("BOOT config file not found.", sourcePath);
        }

        var root = ParseJsonObject(sourcePath);
        root["name"] = configName;
        return root.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// 将内存中的 JSON 快照写入目标 BOOT 配置文件。
    /// </summary>
    public void SaveSnapshotJson(string targetFileName, string snapshotJson)
    {
        var root = JsonNode.Parse(snapshotJson) as JsonObject
            ?? throw new InvalidDataException("Invalid BOOT config snapshot.");
        var targetPath = Path.IsPathRooted(targetFileName)
            ? targetFileName
            : Path.Combine(_paths.BootConfigDirectory, targetFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, root.ToJsonString(JsonOptions));
    }

    private static JsonObject ParseJsonObject(string path) =>
        JsonNode.Parse(
            File.ReadAllText(path),
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) as JsonObject
        ?? throw new InvalidDataException($"Invalid BOOT config: {path}");
}
