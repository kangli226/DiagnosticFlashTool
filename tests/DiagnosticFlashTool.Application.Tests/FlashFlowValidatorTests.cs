using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Flashing;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

/// <summary>
/// The flow validator is the single place that decides whether a BOOT flow can be executed
/// as declared, so the flow editor and the flash executor cannot disagree about it.
/// </summary>
public sealed class FlashFlowValidatorTests
{
    [Fact]
    public void Validate_RejectsSendKeyWithoutPrecedingSeedRequest()
    {
        var issues = Validate(Uds(1, "0x27", "0x02", "AES128_OneFunc"));

        var error = Assert.Single(Errors(issues));
        Assert.Contains("seed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsSendKeyWithoutAlgorithm()
    {
        var issues = Validate(Uds(1, "0x27", "0x01"), Uds(2, "0x27", "0x02"));

        var error = Assert.Single(Errors(issues));
        Assert.Contains("securityAlgorithm", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAlgorithmThatIsNotRegistered()
    {
        var issues = Validate(Uds(1, "0x27", "0x01"), Uds(2, "0x27", "0x02", "TEA_Simplified"));

        var error = Assert.Single(Errors(issues));
        Assert.Contains("TEA_Simplified", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDeclaredCrcAlgorithm()
    {
        // The shipped BOOT configs declare crcAlgorithm while the executor never consumes
        // it, so flashing has to be refused rather than reporting an unverified success.
        var step = Uds(1, "0x10", "0x02");
        step.CrcAlgorithm = "CRC16_DNP";

        var error = Assert.Single(Errors(Validate(step)));
        Assert.Contains("crcAlgorithm", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDeclaredEraseRoutine()
    {
        var step = Uds(1, "0x10", "0x02");
        step.EraseRoutine = "FF00";

        var error = Assert.Single(Errors(Validate(step)));
        Assert.Contains("eraseRoutine", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ReportsReceiveAndVerifyAsWarningsNotErrors()
    {
        var step = Uds(1, "0x10", "0x02");
        step.Receive["62"] = "F190";
        step.Verify.Add("62 F1 90");

        var issues = Validate(step);

        Assert.Empty(Errors(issues));
        Assert.Equal(2, issues.Count(issue => issue.Kind == FlashFlowIssueKind.Warning));
    }

    [Fact]
    public void Validate_RejectsUnknownStepType()
    {
        var step = new FlashStepConfig { Id = 1, Name = "mystery", StepType = "DownloadSomething" };

        var error = Assert.Single(Errors(Validate(step)));
        Assert.Contains("DownloadSomething", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsEmptyFlow()
    {
        var issues = FlashFlowValidator.Validate(new BootConfig { Name = "empty" });

        Assert.Single(Errors(issues));
    }

    [Fact]
    public void Validate_AcceptsPairedSecurityAccessAndPlainUdsSteps()
    {
        var issues = Validate(
            Uds(1, "0x10", "0x02"),
            Uds(2, "0x27", "0x01"),
            Uds(3, "0x27", "0x02", "AES128_OneFunc"),
            new FlashStepConfig { Id = 4, Name = "Download application", StepType = "DownloadApplication" },
            Uds(5, "0x11", "0x01"));

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_WarnsWhenSeedIsNeverUsed()
    {
        var issues = Validate(Uds(1, "0x27", "0x01"));

        Assert.Empty(Errors(issues));
        Assert.Contains(issues, issue => issue.Kind == FlashFlowIssueKind.Warning);
    }

    private static IReadOnlyList<FlashFlowIssue> Validate(params FlashStepConfig[] steps) =>
        FlashFlowValidator.Validate(new BootConfig { Name = "test", Flow = [.. steps] });

    private static List<FlashFlowIssue> Errors(IReadOnlyList<FlashFlowIssue> issues) =>
        issues.Where(issue => issue.Kind == FlashFlowIssueKind.Error).ToList();

    private static FlashStepConfig Uds(int id, string service, string? subService = null, string? algorithm = null) =>
        new()
        {
            Id = id,
            Name = $"step {id}",
            Service = service,
            SubService = subService,
            SecurityAlgorithm = algorithm,
            AddressingMode = "physical"
        };
}
