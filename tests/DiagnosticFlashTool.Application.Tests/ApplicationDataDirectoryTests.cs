using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Infrastructure.Configuration;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

public sealed class ApplicationDataDirectoryTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(), "DiagnosticFlashTool.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ChangeRoot_ExistingRepositoriesAndSettingsFollowSelectedDirectory()
    {
        var paths = CreatePaths();
        var projectRepository = new JsonProjectConfigRepository(paths);
        var bootRepository = new JsonBootConfigRepository(paths);
        projectRepository.SaveAll([new ProjectConfigEntry { ProjectName = "original" }]);
        bootRepository.Save("original.json", new BootConfig { Name = "original" });
        var originalProjectPath = paths.ProjectConfigPath;

        var selectedDirectory = Path.Combine(_temporaryDirectory, "selected");
        paths.ConfigDirectoryOverride = selectedDirectory;
        projectRepository.SaveAll([new ProjectConfigEntry { ProjectName = "selected" }]);
        bootRepository.Save("selected.json", new BootConfig { Name = "selected" });

        Assert.Equal("selected", Assert.Single(projectRepository.LoadAll()).ProjectName);
        Assert.Equal("selected.json", Assert.Single(bootRepository.ListConfigFileNames()));
        Assert.Equal(Path.Combine(selectedDirectory, "settings.json"), paths.SettingsFilePath);
        Assert.Equal(Path.Combine(selectedDirectory, "Scripts"), paths.ScriptDirectory);
        Assert.True(File.Exists(originalProjectPath));
        Assert.True(File.Exists(Path.Combine(paths.DefaultBootConfigDirectory, "original.json")));
    }

    [Fact]
    public void ProjectRepository_RoundTripsCanProtocol()
    {
        var paths = CreatePaths();
        var repository = new JsonProjectConfigRepository(paths);
        repository.SaveAll(
        [
            new ProjectConfigEntry
            {
                ProjectName = "CAN FD ECU",
                CanProtocol = "CANFD",
                BaudRate = "500K"
            }
        ]);

        var project = Assert.Single(repository.LoadAll());
        Assert.Equal("CANFD", project.CanProtocol);
        var json = File.ReadAllText(paths.ProjectConfigPath);
        Assert.DoesNotContain("projectNo", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"CanProtocol\": \"CANFD\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeRoot_PreservesIndependentProjectAndBootOverrides()
    {
        var paths = CreatePaths();
        var projectPath = Path.Combine(_temporaryDirectory, "independent", "projects.json");
        var bootDirectory = Path.Combine(_temporaryDirectory, "independent", "BOOT");
        paths.ProjectConfigPathOverride = projectPath;
        paths.BootConfigDirectoryOverride = bootDirectory;
        paths.ConfigDirectoryOverride = Path.Combine(_temporaryDirectory, "selected");

        Assert.Equal(projectPath, paths.ProjectConfigPath);
        Assert.Equal(bootDirectory, paths.BootConfigDirectory);
        Assert.Equal(Path.Combine(paths.ApplicationDataDirectory, "Scripts"), paths.ScriptDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "resources", "configs"), paths.BuiltInConfigDirectory);
    }

    [Fact]
    public void SaveLocation_RestartLoadsSelectedRootFromFixedEntryPoint()
    {
        var paths = CreatePaths();
        var selectedDirectory = Path.Combine(_temporaryDirectory, "应用数据 with spaces");
        new JsonApplicationDataLocationRepository(paths).Save(selectedDirectory);
        paths.ConfigDirectoryOverride = selectedDirectory;

        var restartedPaths = CreatePaths();
        restartedPaths.ConfigDirectoryOverride = new JsonApplicationDataLocationRepository(restartedPaths).Load();

        Assert.Equal(selectedDirectory, restartedPaths.ApplicationDataDirectory);
        Assert.Equal(Path.Combine(selectedDirectory, "settings.json"), restartedPaths.SettingsFilePath);
        Assert.Equal(paths.ApplicationDataLocationFilePath, restartedPaths.ApplicationDataLocationFilePath);
        Assert.False(File.Exists(Path.Combine(selectedDirectory, "data-location.json")));
    }

    [Fact]
    public void SaveLocation_CanSwitchAgainAndReturnToDefaultDirectory()
    {
        var paths = CreatePaths();
        var repository = new JsonApplicationDataLocationRepository(paths);
        repository.Save(Path.Combine(_temporaryDirectory, "first"));
        repository.Save(Path.Combine(_temporaryDirectory, "second"));
        Assert.Equal(Path.Combine(_temporaryDirectory, "second"), repository.Load());

        repository.Save(paths.DefaultApplicationDataDirectory);

        Assert.Equal(paths.DefaultApplicationDataDirectory, repository.Load());
        Assert.Empty(Directory.EnumerateFiles(paths.DefaultApplicationDataDirectory, "*.tmp"));
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"applicationDataDirectory\": \"relative/data\"}")]
    public void LoadLocation_InvalidEntryUsesDefaultDirectory(string json)
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DefaultApplicationDataDirectory);
        File.WriteAllText(paths.ApplicationDataLocationFilePath, json);

        paths.ConfigDirectoryOverride = new JsonApplicationDataLocationRepository(paths).Load();

        Assert.Equal(paths.DefaultApplicationDataDirectory, paths.ApplicationDataDirectory);
    }

    [Fact]
    public void SaveLocation_InvalidPathPreservesPreviousSelection()
    {
        var paths = CreatePaths();
        var repository = new JsonApplicationDataLocationRepository(paths);
        var selectedDirectory = Path.Combine(_temporaryDirectory, "selected");
        repository.Save(selectedDirectory);

        Assert.Throws<ArgumentException>(() => repository.Save("relative/data"));

        Assert.Equal(selectedDirectory, repository.Load());
    }

    [Fact]
    public void LoadLocation_WithoutEntryDoesNotCreateFiles()
    {
        var paths = CreatePaths();

        Assert.Null(new JsonApplicationDataLocationRepository(paths).Load());
        Assert.False(Directory.Exists(_temporaryDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private AppConfigurationPaths CreatePaths() => new()
    {
        DefaultApplicationDataDirectory = Path.Combine(_temporaryDirectory, "default")
    };
}
