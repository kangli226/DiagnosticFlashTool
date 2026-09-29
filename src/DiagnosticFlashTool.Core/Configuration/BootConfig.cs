using System.Text.Json.Serialization;

namespace DiagnosticFlashTool.Core.Configuration;

/// <summary>
/// 一条 BOOT 刷写流程配置，对应 resources/configs/BOOT/*.json 下的单个文件
/// （由 FlashFlowValidator 校验、FlashFlowExecutor 执行）。
/// </summary>
public sealed class BootConfig
{
    /// <summary>配置名称（BOOT 配置文件名）。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>内嵌脚本元数据；当前无任何消费者，仅在保存时原样保留。</summary>
    [JsonPropertyName("scripts")]
    public List<FlowScriptConfig> Scripts { get; set; } = [];

    /// <summary>流程步骤列表，也是流程编辑器唯一会重写的字段。</summary>
    [JsonPropertyName("flow")]
    public List<FlashStepConfig> Flow { get; set; } = [];
}

/// <summary>
/// BOOT 配置中的内嵌脚本元数据；当前无任何消费者，仅用于旧配置的读写往返，
/// 与磁盘上的 resources/configs/Scripts 算法脚本目录无关。
/// </summary>
public sealed class FlowScriptConfig
{
    /// <summary>脚本标识。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>脚本名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>脚本类别（如安全算法、CRC 算法）。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>脚本说明。</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>脚本正文（Lua 源码）。</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>脚本参数名列表。</summary>
    [JsonPropertyName("parameterNames")]
    public List<string> ParameterNames { get; set; } = [];
}

/// <summary>
/// 刷写流程中的单个步骤，由 BootConfig.Flow 按顺序执行；
/// StepType 为 DownloadDriver/DownloadApplication 时走镜像下载，否则下发普通 UDS 请求。
/// 真正生效：StepType、Service、SubService、Extend、AddressingMode、SecurityAlgorithm、
/// AlgorithmParams、BlockSize。
/// 声明即阻断刷写（校验报错）：CrcAlgorithm、EraseRoutine；
/// 声明被忽略（仅告警）：Receive、Verify、TesterPresentIntervalMs。
/// </summary>
public sealed class FlashStepConfig
{
    /// <summary>内置节点模板的稳定标识；旧配置可由固定字段反向匹配模板。</summary>
    [JsonPropertyName("templateId")]
    public string? TemplateId { get; set; }

    /// <summary>步骤序号；流程编辑器拖拽排序后会重新编号，不是稳定标识。</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>步骤名称，仅用于日志与进度显示。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>DownloadDriver / DownloadApplication 为固件下载步骤，为空则按普通 UDS 步骤执行。</summary>
    [JsonPropertyName("stepType")]
    public string? StepType { get; set; }

    /// <summary>UDS 服务 ID（单字节十六进制，如 0x10）；无法解析时跳过该步骤。</summary>
    [JsonPropertyName("service")]
    public string? Service { get; set; }

    /// <summary>UDS 子功能（如 0x03）；0x27 靠奇偶区分请求 seed（奇数）与发送 key（偶数）。</summary>
    [JsonPropertyName("subService")]
    public string? SubService { get; set; }

    /// <summary>附加参数字节（十六进制字符串），紧随子功能之后下发。</summary>
    [JsonPropertyName("extend")]
    public JsonStringList Extend { get; set; } = [];

    /// <summary>期望响应的匹配规则；当前版本不校验响应，声明只产生告警。</summary>
    [JsonPropertyName("receive")]
    public Dictionary<string, string> Receive { get; set; } = [];

    /// <summary>响应数据校验规则；当前版本不执行，声明只产生告警。</summary>
    [JsonPropertyName("verify")]
    public JsonStringList Verify { get; set; } = [];

    /// <summary>0x36 TransferData 单次传输字节数（可写 0x80，缺省 0xF0），并受 ECU 声明的最大块长与 ISO-TP 上限裁剪。</summary>
    [JsonPropertyName("blockSize")]
    public string? BlockSize { get; set; }

    /// <summary>节点级 Tester Present 周期；当前版本使用会话级周期，此设置会被忽略。</summary>
    [JsonPropertyName("testerPresentIntervalMs")]
    public string? TesterPresentIntervalMs { get; set; }

    /// <summary>擦除例程标识；当前版本不执行擦除，声明即阻断刷写（校验报错）。</summary>
    [JsonPropertyName("eraseRoutine")]
    public string? EraseRoutine { get; set; }

    /// <summary>寻址方式：physical（默认）或 functional。</summary>
    [JsonPropertyName("addressing")]
    public string? AddressingMode { get; set; }

    /// <summary>0x27 发送 key 使用的 seed-key 算法名，必须已在 SeedKeyAlgorithmRegistry 注册。</summary>
    [JsonPropertyName("securityAlgorithm")]
    public string? SecurityAlgorithm { get; set; }

    /// <summary>CRC 校验算法名；当前版本不执行校验，声明即阻断刷写（校验报错）。</summary>
    [JsonPropertyName("crcAlgorithm")]
    public string? CrcAlgorithm { get; set; }

    /// <summary>传给 seed-key 算法的参数（如密钥索引）。</summary>
    [JsonPropertyName("algorithmParams")]
    public JsonStringList AlgorithmParams { get; set; } = [];
}
