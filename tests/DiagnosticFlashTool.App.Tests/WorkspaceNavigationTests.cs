using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiagnosticFlashTool.App.ViewModels;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Flashing;
using DiagnosticFlashTool.Infrastructure.Configuration;
using Xunit;

namespace DiagnosticFlashTool.App.Tests;

public sealed class WorkspaceNavigationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DiagnosticFlashTool.App.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppConfigurationPaths _paths;
    private readonly MainViewModel _viewModel;

    public WorkspaceNavigationTests()
    {
        _paths = new()
        {
            DefaultApplicationDataDirectory = _directory,
            RootDirectory = _directory
        };
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_paths.SettingsFilePath, "{\"LogFileEnabled\":false}");
        var bootRepository = new JsonBootConfigRepository(_paths);
        bootRepository.Save("first.json", new BootConfig { Name = "项目一" });
        bootRepository.Save("second.json", new BootConfig { Name = "项目二" });
        bootRepository.Save("standalone.json", new BootConfig { Name = "独立配置" });
        new JsonProjectConfigRepository(_paths).SaveAll(
        [
            new() { ProjectName = "项目一", ProjectNo = 1, BootConfigFile = "first.json", DriveFilePath = "first.bin" },
            new() { ProjectName = "项目二", ProjectNo = 2, BootConfigFile = "second.json", DriveFilePath = "second.bin" }
        ]);
        _viewModel = new(_paths);
    }

    [Fact]
    public void StandardUser_SeesOneFlashPageForEachProjectAndSharedLogWithSettings()
    {
        Assert.Equal("普通用户", _viewModel.SelectedUserRole);
        Assert.Equal(2, _viewModel.ProjectNavigationItems.Count);
        Assert.All(_viewModel.ProjectNavigationItems, group =>
        {
            Assert.Equal(new[] { 0, 4, 3 }, group.Pages.Select(page => page.PageIndex));
            Assert.All(
                group.Pages.Where(page => page.PageIndex != 0),
                page =>
                {
                    Assert.False(page.CanNavigate);
                    Assert.False(_viewModel.NavigateToProjectPage(page.Project, page.PageIndex));
                });
        });
        var selectedGroup = Assert.Single(_viewModel.ProjectNavigationItems,
            group => ReferenceEquals(group.Project, _viewModel.SelectedProject));
        Assert.Equal(new[] { 0 }, selectedGroup.Pages
            .Where(page => page.IsVisible)
            .Select(page => page.PageIndex));
        Assert.All(_viewModel.ProjectNavigationItems.Where(group => !ReferenceEquals(group.Project, _viewModel.SelectedProject)),
            group => Assert.DoesNotContain(group.Pages, page => page.IsVisible));
        Assert.Equal(6, _viewModel.UdsNavigationItems.Count);
        Assert.DoesNotContain(_viewModel.UdsNavigationItems, page => page.PageIndex == 0);
        Assert.Equal(new[] { 2, 6 }, _viewModel.UdsNavigationItems
            .Where(page => page.IsVisible)
            .Select(page => page.PageIndex));
        Assert.False(_viewModel.NewProjectCommand.CanExecute(null));
        Assert.False(_viewModel.NewFlowCommand.CanExecute(null));
        Assert.False(_viewModel.SaveFlowCommand.CanExecute(null));
        Assert.False(_viewModel.SendManualFrameCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void StandardUser_CannotNavigateToAdministratorPages(int index)
    {
        Assert.False(_viewModel.NavigateToUdsPage(index));
        Assert.True(_viewModel.IsProjectWorkspace);
        Assert.Equal(0, _viewModel.SelectedShellIndex);
        if (index is 1 or 4 or 5)
        {
            _viewModel.SelectedShellIndex = index;
            Assert.Equal(0, _viewModel.SelectedShellIndex);
        }
        _viewModel.NewProjectCommand.Execute(null);
        Assert.False(_viewModel.IsProjectEditorVisible);
    }

    [Fact]
    public void Administrator_OpensAllProjectConfigurationsInUdsWorkspace()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        var second = _viewModel.Projects[1];
        _viewModel.SelectedProject = second;
        Assert.True(_viewModel.NavigateToUdsPage(3));
        _viewModel.EditProjectCommand.Execute(null);
        Assert.False(_viewModel.IsProjectWorkspace);
        Assert.Same(second, _viewModel.SelectedProject);
        Assert.Equal("second.json", _viewModel.SelectedBootConfig);
        Assert.Equal("second.bin", _viewModel.DriverFilePath);
        Assert.True(_viewModel.IsProjectEditorVisible);
        Assert.Equal(second.ProjectName, _viewModel.ProjectEditor.ProjectName);
        Assert.True(_viewModel.SaveProjectEditorCommand.CanExecute(null));
        Assert.True(_viewModel.DeleteProjectEditorCommand.CanExecute(null));
        Assert.Empty(_viewModel.ProjectNavigationItems.SelectMany(group => group.Pages).Where(page => page.IsSelected));
        var selectedPage = Assert.Single(_viewModel.UdsNavigationItems.Where(page => page.IsSelected));
        Assert.Equal("全部项目配置", selectedPage.Title);
        Assert.Equal(3, selectedPage.PageIndex);
    }

    [Fact]
    public void UdsWorkspace_FollowsSelectedProjectForFlashConfiguration()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        var first = _viewModel.SelectedProject;
        Assert.Equal("项目一", first!.ProjectName);
        Assert.True(_viewModel.NavigateToUdsPage(1));
        Assert.Equal(6, _viewModel.UdsNavigationItems.Count(page => page.IsVisible));
        Assert.False(_viewModel.NavigateToUdsPage(0));

        _viewModel.SelectedProject = _viewModel.Projects[1];
        Assert.True(_viewModel.IsUdsWorkspace);
        Assert.Equal("second.json", _viewModel.SelectedBootConfig);
        Assert.Equal("second.bin", _viewModel.DriverFilePath);

        Assert.True(_viewModel.NavigateToProjectPage(_viewModel.Projects[0], 0));
        Assert.Equal("first.json", _viewModel.SelectedBootConfig);
        Assert.Equal("first.bin", _viewModel.DriverFilePath);
        Assert.Equal(2, _viewModel.Projects.Count);
    }

    [Fact]
    public void RoleDowngrade_ReturnsToProjectContextAndKeepsSettingsAccessible()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        Assert.True(_viewModel.NavigateToUdsPage(1));
        _viewModel.SelectedUserRole = "普通用户";
        Assert.True(_viewModel.IsProjectWorkspace);
        Assert.Equal(6, _viewModel.SelectedShellIndex);
        Assert.Equal(6, Assert.Single(_viewModel.UdsNavigationItems.Where(page => page.IsSelected)).PageIndex);
        Assert.False(_viewModel.NavigateToUdsPage(1));
        Assert.False(_viewModel.NewFlowCommand.CanExecute(null));
        Assert.True(_viewModel.NavigateToUdsPage(6));
    }

    [Fact]
    public void BusyWorkspace_BlocksProjectModeRoleAndRefreshChanges()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        var project = _viewModel.SelectedProject;
        _viewModel.IsBusy = true;
        Assert.False(_viewModel.NavigateToProjectPage(_viewModel.Projects[1], 0));
        Assert.False(_viewModel.NavigateToUdsPage(1));
        _viewModel.SelectedUserRole = "普通用户";
        _viewModel.SelectedProject = _viewModel.Projects[1];
        Assert.Equal("系统管理员", _viewModel.SelectedUserRole);
        Assert.Same(project, _viewModel.SelectedProject);
        Assert.False(_viewModel.RefreshProjectsCommand.CanExecute(null));
        Assert.False(_viewModel.NewProjectCommand.CanExecute(null));
        _viewModel.IsBusy = false;
        Assert.True(_viewModel.NavigateToUdsPage(1));
    }

    [Fact]
    public void SharedLog_ShowsAndClearsAllProjectRecords()
    {
        _viewModel.LogEntries.Clear();
        _viewModel.LogEntries.Add(new(DateTime.Now, DiagnosticStatusKind.Neutral, "项目一的记录") { ProjectNo = 1, ProjectName = "项目一" });
        _viewModel.LogEntries.Add(new(DateTime.Now, DiagnosticStatusKind.Error, "项目二的记录") { ProjectNo = 2, ProjectName = "项目二" });
        _viewModel.LogEntries.Add(new(DateTime.Now, DiagnosticStatusKind.Neutral, "全局记录"));
        Assert.False(_viewModel.NavigateToProjectPage(_viewModel.Projects[1], 2));
        Assert.True(_viewModel.NavigateToUdsPage(2));
        Assert.False(_viewModel.IsProjectWorkspace);
        Assert.True(_viewModel.FilteredLogEntries.Count >= 3);
        Assert.Contains(_viewModel.FilteredLogEntries, entry => entry.Message == "项目一的记录");
        Assert.Contains(_viewModel.FilteredLogEntries, entry => entry.Message == "项目二的记录");
        Assert.Contains(_viewModel.FilteredLogEntries, entry => entry.Message == "全局记录");
        Assert.True(_viewModel.WorkspaceLogEntries.Count >= 3);
        _viewModel.ClearLogCommand.Execute(null);
        Assert.Empty(_viewModel.LogEntries);
        Assert.Empty(_viewModel.FilteredLogEntries);
    }

    [Fact]
    public async Task Role_SurvivesRestartAndRestoresFirstProject()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        _viewModel.SelectedProject = _viewModel.Projects[1];
        var settings = JsonDocument.Parse(File.ReadAllText(_paths.SettingsFilePath));
        Assert.Equal("系统管理员", settings.RootElement.GetProperty("UserRole").GetString());
        Assert.False(settings.RootElement.TryGetProperty("UdsConfiguration", out _));
        Assert.False(settings.RootElement.TryGetProperty("UdsConfigurationSource", out _));

        var restarted = new MainViewModel(_paths);
        Assert.True(restarted.IsSystemAdministrator);
        Assert.Equal("项目一", restarted.SelectedProject!.ProjectName);
        Assert.Equal("first.json", restarted.SelectedBootConfig);
        Assert.Equal("first.bin", restarted.DriverFilePath);
        await restarted.DisposeAsync();
    }

    [Fact]
    public void ProjectSave_OnlyChangesCurrentProjectAndPreservesItsLogs()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        _viewModel.SelectedProject = _viewModel.Projects[1];
        _viewModel.NavigateToUdsPage(3);
        _viewModel.EditProjectCommand.Execute(null);
        _viewModel.ProjectEditor.ProjectName = "项目二已更新";
        _viewModel.ProjectEditor.ProjectNoText = "12";
        _viewModel.SaveProjectEditorCommand.Execute(null);

        var saved = new JsonProjectConfigRepository(_paths).LoadAll();
        Assert.Equal("项目一", saved[0].ProjectName);
        Assert.Equal("项目二已更新", saved[1].ProjectName);
        Assert.Equal(12, saved[1].ProjectNo);
        Assert.False(_viewModel.IsProjectEditorVisible);
        Assert.DoesNotContain(_viewModel.LogEntries, entry => entry.ProjectNo == 2);
        Assert.Contains(_viewModel.WorkspaceLogEntries, entry => entry.ProjectNo == 12);
        Assert.Equal("项目二已更新", _viewModel.SelectedProject!.ProjectName);
    }

    [Fact]
    public void NoProjects_LeavesFlashUnavailableInUdsWorkspace()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        new JsonProjectConfigRepository(_paths).SaveAll([]);
        _viewModel.RefreshProjectsCommand.Execute(null);
        Assert.Empty(_viewModel.Projects);
        Assert.False(_viewModel.NavigateToUdsPage(0));
        Assert.True(_viewModel.NavigateToUdsPage(1));
        Assert.Null(_viewModel.SelectedProject);
        Assert.Equal("请先选择项目", _viewModel.FlashCommunicationSummaryText);
        Assert.False(_viewModel.StartFlashCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void UdsProjectSelection_LinksBootFirmwareProtocolAndBaudRate(int pageIndex)
    {
        _viewModel.SelectedUserRole = "系统管理员";
        var second = _viewModel.Projects[1];
        second.PhysicalRequestId = "0x700";
        second.FunctionalRequestId = "0x7DF";
        second.ResponseAddressId = "0x708";
        second.CanProtocol = "CANFD";
        second.BaudRate = "250K";
        second.FlashFilePath = "second-app.bin";
        _viewModel.SelectedBaudRateMode = "跟随项目";

        Assert.True(_viewModel.NavigateToUdsPage(pageIndex));
        _viewModel.SelectedProject = second;

        Assert.True(_viewModel.IsUdsWorkspace);
        Assert.Equal(pageIndex, _viewModel.SelectedShellIndex);
        Assert.Equal("second.json", _viewModel.SelectedBootConfig);
        Assert.Equal("second.bin", _viewModel.DriverFilePath);
        Assert.Equal("second-app.bin", _viewModel.ApplicationFilePath);
        Assert.Equal("250K", _viewModel.SelectedBaudRate);
        Assert.True(_viewModel.IsCanFdRequested);
        Assert.Contains("0x700", _viewModel.FlashCommunicationSummaryText);
        Assert.Contains("0x708", _viewModel.FlashCommunicationSummaryText);
        Assert.Contains(second.ProjectName, _viewModel.StatusBarProjectText);
        Assert.All(_viewModel.ProjectNavigationItems.SelectMany(group => group.Pages), page => Assert.False(page.IsSelected));
        Assert.Equal(pageIndex, Assert.Single(_viewModel.UdsNavigationItems.Where(page => page.IsSelected)).PageIndex);
    }

    [Fact]
    public void BusyUdsWorkspace_BlocksConfigurationChangesAndKeepsFrameToolsAvailable()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        _viewModel.NavigateToUdsPage(1);
        var project = _viewModel.SelectedProject;
        _viewModel.IsBusy = true;
        _viewModel.SelectedProject = _viewModel.Projects[1];

        Assert.Same(project, _viewModel.SelectedProject);
        Assert.Equal("first.json", _viewModel.SelectedBootConfig);
        Assert.True(_viewModel.ToggleFrameCaptureCommand.CanExecute(null));
        Assert.True(_viewModel.ToggleFrameFilterCommand.CanExecute(null));
        Assert.True(_viewModel.ExportFramesCommand.CanExecute(null));
        Assert.True(_viewModel.ClearFramesCommand.CanExecute(null));
        _viewModel.IsBusy = false;
    }

    [Fact]
    public void NewProjectFlow_InUdsWorkspaceCreatesAndSavesProjectDraft()
    {
        _viewModel.SelectedUserRole = "系统管理员";
        _viewModel.NavigateToUdsPage(4);
        _viewModel.NewFlowCommand.Execute(null);
        _viewModel.NewFlowProjectName = "新增项目流程";
        _viewModel.CreateFlowProjectFromTemplate = false;
        _viewModel.ConfirmNewFlowProjectCommand.Execute(null);

        Assert.Equal(3, _viewModel.Projects.Count);
        Assert.Equal("新增项目流程", _viewModel.SelectedProject!.ProjectName);
        Assert.Equal("新增项目流程.json", _viewModel.SelectedBootConfig);
        Assert.False(File.Exists(Path.Combine(_paths.BootConfigDirectory, "新增项目流程.json")));
        _viewModel.AddFlowStepCommand.Execute(null);
        _viewModel.SelectedFlowStepTemplate = _viewModel.FlowStepTemplates.First(template => template.Id == "session.extended");
        _viewModel.ConfirmAddFlowStepCommand.Execute(null);
        _viewModel.SaveFlowCommand.Execute(null);

        Assert.True(File.Exists(Path.Combine(_paths.BootConfigDirectory, "新增项目流程.json")));
        Assert.Contains(new JsonProjectConfigRepository(_paths).LoadAll(), project => project.ProjectName == "新增项目流程");
        Assert.Single(_viewModel.FlowRows);
    }

    [Fact]
    public async Task UdsProjectFlash_UsesProjectAddressesAndFirmwareWithMock()
    {
        var sessionStep = BuiltInFlowStepTemplateCatalog.FindById("session.extended")!.CreateStep(1);
        var downloadStep = BuiltInFlowStepTemplateCatalog.FindById("download.application")!.CreateStep(2);
        new JsonBootConfigRepository(_paths).Save("project-mock.json", new BootConfig { Flow = [sessionStep, downloadStep] });
        var applicationPath = Path.Combine(_directory, "project-app.bin");
        await File.WriteAllBytesAsync(applicationPath, Enumerable.Range(0, 64).Select(value => (byte)value).ToArray());
        var project = _viewModel.Projects[1];
        project.BootConfigFile = "project-mock.json";
        project.DriveFilePath = string.Empty;
        project.FlashFilePath = applicationPath;
        project.PhysicalRequestId = "0x710";
        project.FunctionalRequestId = "0x7DF";
        project.ResponseAddressId = "0x718";
        _viewModel.SelectedUserRole = "系统管理员";
        _viewModel.NavigateToUdsPage(1);
        _viewModel.SelectedProject = project;
        _viewModel.SelectedDeviceType = "Mock";
        _viewModel.ConnectCommand.Execute(null);
        await WaitUntilAsync(() => !_viewModel.ConnectCommand.IsRunning);
        Assert.True(_viewModel.IsConnected, _viewModel.StatusText);
        _viewModel.StartFlashCommand.Execute(null);
        await WaitUntilAsync(() => !_viewModel.StartFlashCommand.IsRunning);

        Assert.Equal(DiagnosticStatusKind.Success, _viewModel.DownloadStatusKind);
        Assert.Equal(100, _viewModel.Progress);
        Assert.Contains(_viewModel.Frames, frame => frame.IdText == "0x00000710");
        Assert.Contains(_viewModel.Frames, frame => frame.IdText == "0x00000718");
        Assert.Contains("刷写完成", _viewModel.DownloadInfoText);
        Assert.Contains(_viewModel.LogEntries, entry => entry.Message.Contains("刷写完成") && entry.ProjectNo == project.ProjectNo);
    }

    [Fact]
    public void SavingUdsProjectFlow_PreservesSelectionScriptsAndUneditedFields()
    {
        new JsonBootConfigRepository(_paths).Save("first.json", new BootConfig
        {
            Flow = [BuiltInFlowStepTemplateCatalog.FindById("session.extended")!.CreateStep(1)],
            Scripts = [new() { Id = "keep-script", Code = "keep source" }]
        });
        var path = Path.Combine(_paths.BootConfigDirectory, "first.json");
        var original = JsonNode.Parse(File.ReadAllText(path))!;
        original["scripts"] = JsonSerializer.SerializeToNode(new[] { new FlowScriptConfig { Id = "keep-script", Code = "keep source" } });
        original["vendorData"] = JsonValue.Create("保留扩展数据");
        File.WriteAllText(path, original.ToJsonString());
        _viewModel.SelectedUserRole = "系统管理员";
        _viewModel.NavigateToUdsPage(4);
        var selectedProject = _viewModel.SelectedProject;
        _viewModel.FlowRows[0].TesterPresentIntervalMs = "1500";
        var replaceCount = 0;
        _viewModel.Projects.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            {
                replaceCount++;
            }
        };

        _viewModel.SaveFlowCommand.Execute(null);

        Assert.Equal(DiagnosticStatusKind.Success, _viewModel.StatusKind);
        Assert.Equal(0, replaceCount);
        Assert.Same(selectedProject, _viewModel.SelectedProject);
        Assert.Equal("first.json", _viewModel.SelectedBootConfig);
        Assert.Equal("1500", Assert.Single(_viewModel.FlowRows).TesterPresentIntervalMs);
        var saved = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("保留扩展数据", saved["vendorData"]!.GetValue<string>());
        Assert.Equal("keep source", saved["scripts"]![0]!["code"]!.GetValue<string>());
        Assert.Equal("1500", saved["flow"]![0]!["testerPresentIntervalMs"]!.GetValue<string>());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public void Dispose()
    {
        _viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Directory.Delete(_directory, recursive: true);
    }
}
