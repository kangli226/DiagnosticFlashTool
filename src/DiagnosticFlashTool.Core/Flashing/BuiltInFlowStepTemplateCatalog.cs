using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Core.Flashing;

public enum FlowStepTemplateAlgorithmUsage
{
    None,
    Security
}

/// <summary>不依赖 UI 的内置流程节点定义，是通用主程序可编辑节点的白名单。</summary>
public sealed record FlowStepTemplateDefinition(
    string Id,
    string Category,
    string Title,
    string StepName,
    string Description,
    string? StepType,
    string? Service,
    string? SubService,
    IReadOnlyList<string> Extend,
    FlowStepTemplateAlgorithmUsage AlgorithmUsage = FlowStepTemplateAlgorithmUsage.None)
{
    public FlashStepConfig CreateStep(int stepId)
    {
        var extend = new JsonStringList();
        foreach (var value in Extend)
        {
            extend.Add(value);
        }

        return new FlashStepConfig
        {
            TemplateId = Id,
            Id = stepId,
            Name = StepName,
            StepType = StepType,
            Service = Service,
            SubService = SubService,
            Extend = extend,
            AddressingMode = "physical"
        };
    }
}

public static class BuiltInFlowStepTemplateCatalog
{
    public static IReadOnlyList<FlowStepTemplateDefinition> Templates { get; } =
    [
        Template(
            "download.driver",
            "下载流程",
            "驱动下载",
            "驱动下载",
            "DownloadDriver",
            stepType: FlashStepTypes.DownloadDriver),
        Template(
            "download.application",
            "下载流程",
            "应用下载",
            "应用下载",
            "DownloadApplication",
            stepType: FlashStepTypes.DownloadApplication),
        Template("session.default", "会话控制", "进入默认会话", "进入默认会话", "0x10 / 0x01", "0x10", "0x01"),
        Template("session.programming", "会话控制", "进入编程会话", "进入编程会话", "0x10 / 0x02", "0x10", "0x02"),
        Template("session.extended", "会话控制", "进入扩展诊断会话", "进入扩展诊断会话", "0x10 / 0x03", "0x10", "0x03"),
        Template("reset.hard", "ECU 复位", "硬复位", "硬复位", "0x11 / 0x01", "0x11", "0x01"),
        Template("security.level1.seed", "安全访问", "请求 Seed（Level 1）", "请求Seed(Level1)", "0x27 / 0x01", "0x27", "0x01"),
        Template(
            "security.level1.key",
            "安全访问",
            "发送 Key（Level 1）",
            "发送Key(Level1)",
            "0x27 / 0x02",
            "0x27",
            "0x02",
            algorithmUsage: FlowStepTemplateAlgorithmUsage.Security),
        Template("security.level3.seed", "安全访问", "请求 Seed（Level 3）", "请求Seed(Level3)", "0x27 / 0x05", "0x27", "0x05"),
        Template(
            "security.level3.key",
            "安全访问",
            "发送 Key（Level 3）",
            "发送Key(Level3)",
            "0x27 / 0x06",
            "0x27",
            "0x06",
            algorithmUsage: FlowStepTemplateAlgorithmUsage.Security),
        Template("dtc.disable", "DTC 设置", "关闭 DTC", "关闭DTC", "0x85 / 0x02", "0x85", "0x02"),
        Template("dtc.enable", "DTC 设置", "开启 DTC", "开启DTC", "0x85 / 0x01", "0x85", "0x01"),
        Template("communication.disable", "通信控制", "关闭收发", "关闭收发", "0x28 / 0x03 + 03", "0x28", "0x03", ["0x03"]),
        Template("communication.enable", "通信控制", "开启收发", "开启收发", "0x28 / 0x00 + 03", "0x28", "0x00", ["0x03"]),
        Template("routine.start.ff01", "例程控制", "启动例程（FF01）", "启动例程", "0x31 / 0x01 + FF 01", "0x31", "0x01", ["0xFF", "0x01"]),
        Template("routine.start.aaaa", "例程控制", "启动例程（AAAA）", "启动例程", "0x31 / 0x01 + AA AA", "0x31", "0x01", ["0xAA", "0xAA"]),
        Template("routine.vendor.ff01", "例程控制", "厂商例程（FF/01）", "启动例程", "0x31 / 0xFF + 01", "0x31", "0xFF", ["0x01"])
    ];

    public static FlowStepTemplateDefinition? FindById(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        return Templates.FirstOrDefault(template =>
            string.Equals(template.Id, templateId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按模板固定字段匹配旧节点；不会忽略已声明但错误的 templateId。</summary>
    public static FlowStepTemplateDefinition? FindMatchingTemplate(FlashStepConfig step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (!string.IsNullOrWhiteSpace(step.TemplateId))
        {
            var declared = FindById(step.TemplateId);
            return declared is not null && MatchesFixedFields(step, declared) ? declared : null;
        }

        return Templates.FirstOrDefault(template => MatchesFixedFields(step, template));
    }

    public static bool MatchesFixedFields(FlashStepConfig step, FlowStepTemplateDefinition template)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(template);

        return string.Equals(step.Name?.Trim(), template.StepName, StringComparison.Ordinal)
            && StepTypesEqual(step.StepType, template.StepType)
            && HexValuesEqual(step.Service, template.Service)
            && HexValuesEqual(step.SubService, template.SubService)
            && ExtendValuesEqual(step.Extend, template.Extend);
    }

    private static FlowStepTemplateDefinition Template(
        string id,
        string category,
        string title,
        string stepName,
        string description,
        string? service = null,
        string? subService = null,
        IReadOnlyList<string>? extend = null,
        string? stepType = null,
        FlowStepTemplateAlgorithmUsage algorithmUsage = FlowStepTemplateAlgorithmUsage.None) =>
        new(
            id,
            category,
            title,
            stepName,
            description,
            stepType,
            service,
            subService,
            extend ?? [],
            algorithmUsage);

    private static bool StepTypesEqual(string? actual, string? expected)
    {
        return string.Equals(
            string.IsNullOrWhiteSpace(actual) ? null : actual.Trim(),
            string.IsNullOrWhiteSpace(expected) ? null : expected.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool HexValuesEqual(string? actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return string.IsNullOrWhiteSpace(actual);
        }

        return HexUtil.TryParseByte(actual, out var actualByte)
            && HexUtil.TryParseByte(expected, out var expectedByte)
            && actualByte == expectedByte;
    }

    private static bool ExtendValuesEqual(IEnumerable<string> actual, IEnumerable<string> expected)
    {
        return TryParseBytes(actual, out var actualBytes)
            && TryParseBytes(expected, out var expectedBytes)
            && actualBytes.SequenceEqual(expectedBytes);
    }

    private static bool TryParseBytes(IEnumerable<string> values, out byte[] bytes)
    {
        try
        {
            bytes = values.SelectMany(HexUtil.ParseBytes).ToArray();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            bytes = [];
            return false;
        }
    }
}
