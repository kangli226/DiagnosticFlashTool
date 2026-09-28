using System.Text.Json;
using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.Infrastructure.Configuration;

/// <summary>
/// 在 JSON 文件与 <see cref="BootConfig"/> 模型之间加载和保存 BOOT 刷写流程配置。
/// </summary>
/// <remarks>
/// 相对文件名以 <see cref="AppConfigurationPaths.BootConfigDirectory"/> 为基准；
/// 绝对路径则直接使用。保存操作会重新序列化并覆盖整个目标文件。
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
    /// 将完整的 BOOT 刷写流程配置序列化并覆盖写入 JSON 文件。
    /// </summary>
    /// <param name="fileName">配置文件名，或配置文件的绝对路径。</param>
    /// <param name="config">要保存的完整 BOOT 配置。</param>
    /// <remarks>
    /// 本方法不会与现有 JSON 合并。调用方如需只修改 <c>flow</c>，应先加载原配置、
    /// 替换 <see cref="BootConfig.Flow"/>，再将完整对象传入本方法。
    /// </remarks>
    public void Save(string fileName, BootConfig config)
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(_paths.BootConfigDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }
}
