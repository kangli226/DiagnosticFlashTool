using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Firmware;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

public sealed class ProjectConfigEntryFirmwareStatusTests
{
    private readonly FirmwareLoader _firmwareLoader = new();

    [Fact]
    public void RefreshFirmwareFileStatuses_UsesDashWhenBootDoesNotDeclareFileOption()
    {
        var project = new ProjectConfigEntry();

        project.RefreshFirmwareFileStatuses(new BootConfig(), _firmwareLoader);

        Assert.Equal("-", project.DriverFileStatusIcon);
        Assert.Equal("-", project.ApplicationFileStatusIcon);
        Assert.False(project.IsDriverFileError);
        Assert.False(project.IsApplicationFileError);
    }

    [Fact]
    public void RefreshFirmwareFileStatuses_UsesErrorWhenRequiredFileIsMissing()
    {
        var project = new ProjectConfigEntry();
        var config = CreateBootConfig("DownloadDriver", "DownloadApplication");

        project.RefreshFirmwareFileStatuses(config, _firmwareLoader);

        Assert.Equal("×", project.DriverFileStatusIcon);
        Assert.Equal("×", project.ApplicationFileStatusIcon);
        Assert.True(project.IsDriverFileError);
        Assert.True(project.IsApplicationFileError);
    }

    [Fact]
    public void RefreshFirmwareFileStatuses_UsesCheckOnlyForLoadableNonEmptyFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(filePath, [0x01, 0x02]);
            var project = new ProjectConfigEntry { DriveFilePath = filePath };

            project.RefreshFirmwareFileStatuses(
                CreateBootConfig("DownloadDriver"),
                _firmwareLoader);

            Assert.Equal("✓", project.DriverFileStatusIcon);
            Assert.True(project.IsDriverFileAvailable);
            Assert.False(project.IsDriverFileError);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void RefreshFirmwareFileStatuses_UsesErrorForUnsupportedFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(filePath, "invalid firmware");
            var project = new ProjectConfigEntry { FlashFilePath = filePath };

            project.RefreshFirmwareFileStatuses(
                CreateBootConfig("DownloadApplication"),
                _firmwareLoader);

            Assert.Equal("×", project.ApplicationFileStatusIcon);
            Assert.True(project.IsApplicationFileError);
            Assert.Contains("App 文件无效", project.ApplicationFileStatusText);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static BootConfig CreateBootConfig(params string[] stepTypes)
    {
        return new BootConfig
        {
            Flow = stepTypes.Select((stepType, index) => new FlashStepConfig
            {
                Id = index + 1,
                StepType = stepType
            }).ToList()
        };
    }
}
