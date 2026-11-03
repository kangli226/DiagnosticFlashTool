using System.Text.Json;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Infrastructure.Configuration;
using Xunit;

namespace DiagnosticFlashTool.Application.Tests;

public sealed class JsonBootConfigRepositoryTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "DiagnosticFlashTool.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_ExistingConfig_ReplacesOnlyFlow()
    {
        var repository = CreateRepository();
        var path = Path.Combine(_tempDirectory, "source.json");
        File.WriteAllText(
            path,
            """
            {
              "name": "source",
              "scripts": [{ "id": "script-1", "extension": "keep" }],
              "customRoot": { "enabled": true },
              "flow": [{ "id": 1, "name": "old", "customStep": "replace" }]
            }
            """);

        repository.Save(
            "source.json",
            new BootConfig
            {
                Name = "must-not-replace-source-name",
                Flow = [new FlashStepConfig { Id = 1, Name = "new", Service = "0x10" }]
            });

        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        var root = saved.RootElement;
        Assert.Equal("source", root.GetProperty("name").GetString());
        Assert.Equal("keep", root.GetProperty("scripts")[0].GetProperty("extension").GetString());
        Assert.True(root.GetProperty("customRoot").GetProperty("enabled").GetBoolean());
        Assert.Equal("new", root.GetProperty("flow")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void SaveSnapshot_CreatesIndependentCopyAndPreservesExtensionFields()
    {
        var repository = CreateRepository();
        var sourcePath = Path.Combine(_tempDirectory, "template.json");
        File.WriteAllText(
            sourcePath,
            """
            {
              // A snapshot accepts the same relaxed JSON syntax as normal BOOT loading.
              "name": "template",
              "customRoot": { "version": 3 },
              "flow": [{ "id": 1, "name": "step", "vendorOption": "keep" }],
            }
            """);

        repository.SaveSnapshot("template.json", "project-a.json", "project-a");

        using var source = JsonDocument.Parse(
            File.ReadAllText(sourcePath),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDirectory, "project-a.json")));

        Assert.Equal("template", source.RootElement.GetProperty("name").GetString());
        Assert.Equal("project-a", snapshot.RootElement.GetProperty("name").GetString());
        Assert.Equal(3, snapshot.RootElement.GetProperty("customRoot").GetProperty("version").GetInt32());
        Assert.Equal("keep", snapshot.RootElement.GetProperty("flow")[0].GetProperty("vendorOption").GetString());
    }

    [Fact]
    public void SnapshotJson_DelayedSave_UsesContentCapturedAtCreation()
    {
        var repository = CreateRepository();
        var sourcePath = Path.Combine(_tempDirectory, "template.json");
        var targetPath = Path.Combine(_tempDirectory, "delayed.json");
        File.WriteAllText(
            sourcePath,
            """
            {
              "name": "template",
              "customRoot": { "version": 1 },
              "flow": [{ "id": 1, "name": "captured" }]
            }
            """);

        var snapshotJson = repository.CreateSnapshotJson("template.json", "delayed");
        Assert.False(File.Exists(targetPath));

        File.WriteAllText(
            sourcePath,
            """
            {
              "name": "template-changed",
              "customRoot": { "version": 2 },
              "flow": [{ "id": 1, "name": "changed" }]
            }
            """);
        repository.SaveSnapshotJson("delayed.json", snapshotJson);

        using var snapshot = JsonDocument.Parse(File.ReadAllText(targetPath));
        Assert.Equal("delayed", snapshot.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, snapshot.RootElement.GetProperty("customRoot").GetProperty("version").GetInt32());
        Assert.Equal("captured", snapshot.RootElement.GetProperty("flow")[0].GetProperty("name").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private JsonBootConfigRepository CreateRepository()
    {
        Directory.CreateDirectory(_tempDirectory);
        return new JsonBootConfigRepository(new AppConfigurationPaths
        {
            BootConfigDirectoryOverride = _tempDirectory
        });
    }
}
