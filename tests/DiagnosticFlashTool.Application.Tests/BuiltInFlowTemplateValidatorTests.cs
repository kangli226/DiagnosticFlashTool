using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Flashing;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

public sealed class BuiltInFlowTemplateValidatorTests
{
    [Fact]
    public void Catalog_HasUniqueStableIdsAndCategories()
    {
        var templates = BuiltInFlowStepTemplateCatalog.Templates;

        Assert.NotEmpty(templates);
        Assert.Equal(
            templates.Count,
            templates.Select(template => template.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(templates, template =>
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Id));
            Assert.False(string.IsNullOrWhiteSpace(template.Category));
            Assert.False(string.IsNullOrWhiteSpace(template.Title));
        });
    }

    [Fact]
    public void Validate_AcceptsEveryBuiltInTemplate()
    {
        foreach (var template in BuiltInFlowStepTemplateCatalog.Templates)
        {
            var config = Config(template.CreateStep(1));

            var issues = BuiltInFlowTemplateValidator.Validate(config);
            if (template.AlgorithmUsage == FlowStepTemplateAlgorithmUsage.CrcVerification)
            {
                Assert.Single(issues);
                Assert.Contains("占位符", issues[0].Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Empty(issues);
            }
        }
    }

    [Fact]
    public void Validate_AcceptsLegacyStepWithoutTemplateIdWhenFixedFieldsMatch()
    {
        var step = Template("communication.disable").CreateStep(1);
        step.TemplateId = null;
        step.Service = "40";
        step.SubService = "3";
        step.Extend = Values("03");

        var issues = BuiltInFlowTemplateValidator.Validate(Config(step));

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("0x01", "0xFF, 0x01", "routine.start.ff01")]
    [InlineData("0x01", "0xAA 0xAA", "routine.start.aaaa")]
    [InlineData("0xFF", "0x01", "routine.vendor.ff01")]
    public void Catalog_DistinguishesRoutineVariants(
        string subService,
        string extend,
        string expectedTemplateId)
    {
        var step = new FlashStepConfig
        {
            Id = 1,
            Name = "启动例程",
            Service = "0x31",
            SubService = subService,
            Extend = Values(extend),
            AddressingMode = "physical"
        };

        var template = BuiltInFlowStepTemplateCatalog.FindMatchingTemplate(step);

        Assert.NotNull(template);
        Assert.Equal(expectedTemplateId, template.Id);
    }

    [Fact]
    public void Validate_RejectsCustomUdsNode()
    {
        var step = new FlashStepConfig
        {
            Id = 1,
            Name = "读取数据",
            Service = "0x22",
            Extend = Values("F1 90"),
            AddressingMode = "physical"
        };

        var error = Assert.Single(BuiltInFlowTemplateValidator.Validate(Config(step)));

        Assert.Contains("不允许自定义", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("extend")]
    [InlineData("addressing")]
    public void Validate_RejectsChangesToTemplateControlledProtocol(string change)
    {
        var step = Template("communication.disable").CreateStep(1);
        switch (change)
        {
            case "name":
                step.Name = "自定义名称";
                break;
            case "extend":
                step.Extend = Values("0x04");
                break;
            case "addressing":
                step.AddressingMode = "functional";
                break;
        }

        var issues = BuiltInFlowTemplateValidator.Validate(Config(step));

        Assert.Contains(issues, issue => issue.Kind == FlashFlowIssueKind.Error);
    }

    [Fact]
    public void Validate_RejectsUnknownTemplateId()
    {
        var step = Template("session.programming").CreateStep(1);
        step.TemplateId = "session.custom";

        var error = Assert.Single(BuiltInFlowTemplateValidator.Validate(Config(step)));

        Assert.Contains("未知", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsTemplateIdThatDoesNotMatchFixedFields()
    {
        var step = Template("session.programming").CreateStep(1);
        step.TemplateId = "session.default";

        var error = Assert.Single(BuiltInFlowTemplateValidator.Validate(Config(step)));

        Assert.Contains("不一致", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsSecuritySettingsOnNonSendKeyNode()
    {
        var step = Template("session.programming").CreateStep(1);
        step.SecurityAlgorithm = "AES128_OneFunc";
        step.AlgorithmParams = Values("key-index-1");

        var issues = BuiltInFlowTemplateValidator.Validate(Config(step));

        Assert.Contains(issues, issue => issue.Message.Contains("securityAlgorithm", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Message.Contains("algorithmParams", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowsSecuritySettingsOnSendKeyNode()
    {
        var step = Template("security.level1.key").CreateStep(1);
        step.SecurityAlgorithm = "AES128_OneFunc";
        step.AlgorithmParams = Values("key-index-1");

        Assert.Empty(BuiltInFlowTemplateValidator.Validate(Config(step)));
    }

    [Fact]
    public void Validate_AllowsCrcOnDownloadNode()
    {
        var step = Template("download.application").CreateStep(1);
        step.CrcAlgorithm = "CRC16_DNP";

        Assert.Empty(BuiltInFlowTemplateValidator.Validate(Config(step)));
    }

    [Fact]
    public void Validate_AcceptsConfiguredCrcVerificationTemplate()
    {
        var step = Template("routine.crc.application").CreateStep(1);
        step.Extend = Values(
            "0xF2, 0x04",
            "${application.start:u32be}",
            "${application.length:u32be}",
            "${application.crc:u32be}");

        Assert.Empty(BuiltInFlowTemplateValidator.Validate(Config(step)));
    }

    private static FlowStepTemplateDefinition Template(string id) =>
        Assert.Single(BuiltInFlowStepTemplateCatalog.Templates, template => template.Id == id);

    private static BootConfig Config(params FlashStepConfig[] steps) =>
        new() { Name = "test", Flow = [.. steps] };

    private static JsonStringList Values(params string[] values)
    {
        var list = new JsonStringList();
        foreach (var value in values)
        {
            list.Add(value);
        }

        return list;
    }
}
