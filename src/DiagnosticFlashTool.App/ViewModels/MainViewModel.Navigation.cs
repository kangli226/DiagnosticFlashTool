using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using DiagnosticFlashTool.App.Utilities;
using DiagnosticFlashTool.App.Views.Controls;
using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed partial class MainViewModel
{
    internal const int ProjectFirmwareFlashPageIndex = 0;
    internal const int ProjectFlashFlowPageIndex = 7;
    private const string StandardRoleText = "普通用户";
    private const string AdministratorRoleText = "系统管理员";
    private string _selectedUserRole = StandardRoleText;
    private bool _isProjectWorkspace = true;
    private string _projectNavigationFilter = string.Empty;

    public IReadOnlyList<string> UserRoles { get; } = [StandardRoleText, AdministratorRoleText];
    public ObservableCollection<ProjectNavigationGroupViewModel> ProjectNavigationItems { get; } = [];
    public ICollectionView ProjectNavigationView { get; private set; } = null!;
    public ObservableCollection<ShellNavigationItemViewModel> UdsNavigationItems { get; } = [];
    public int VisibleToolCount => UdsNavigationItems.Count(page => page.IsVisible);
    public ObservableCollection<SystemLogEntry> WorkspaceLogEntries { get; } = [];
    public RelayCommand OpenActiveProjectConfigCommand { get; private set; } = null!;
    public bool IsProjectWorkspace => _isProjectWorkspace;
    public bool IsUdsWorkspace => !IsProjectWorkspace;
    public bool IsSystemAdministrator => SelectedUserRole == AdministratorRoleText;
    public bool CanChangeWorkspace => !IsBusy;
    public bool CanManageProjects => IsSystemAdministrator && CanChangeWorkspace;
    public bool HasProjects => Projects.Count > 0;
    public bool HasNoProjects => !HasProjects;
    public bool HasSelectedProject => SelectedProject is not null;
    private ProjectConfigEntry? ActiveFlashConfiguration => SelectedProject;
    private bool IsProjectFlowDraft => _draftFlowProject is not null
        && ReferenceEquals(SelectedProject, _draftFlowProject);
    private bool CanEditFlow => IsSystemAdministrator && IsUdsWorkspace && CanChangeWorkspace;

    public string WorkspaceDescription => IsProjectWorkspace
        ? SelectedProject is null ? "请选择或新增一个项目" : $"项目：{SelectedProject.ProjectName}"
        : SelectedProject is null ? "工具与设置（未选择项目）" : $"工具与设置 / 项目：{SelectedProject.ProjectName}";

    public string FlashCommunicationSummaryText => ActiveFlashConfiguration is not { } configuration
        ? "请先选择项目"
        : $"物理请求 {configuration.PhysicalRequestId} / 功能请求 {configuration.FunctionalRequestId} / 响应 {configuration.ResponseAddressId} / {configuration.CanProtocol} / {configuration.BaudRate}";

    public string RoleDescription => IsSystemAdministrator
        ? "可使用各项目的固件刷写并查看刷写流程，也可在「工具与设置」中管理项目和流程配置。"
        : "可使用各项目的固件刷写、查看刷写流程，并访问操作日志和系统设置。";

    public string ProjectNavigationFilter
    {
        get => _projectNavigationFilter;
        set
        {
            if (SetProperty(ref _projectNavigationFilter, value))
            {
                var filter = value.Trim();
                ProjectNavigationView.Filter = filter.Length == 0
                    ? null
                    : item => item is ProjectNavigationGroupViewModel group
                        && group.Project.ProjectName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
            }
        }
    }

    public string SelectedUserRole
    {
        get => _selectedUserRole;
        set
        {
            if (!UserRoles.Contains(value) || value == _selectedUserRole)
            {
                return;
            }
            if (!CanChangeWorkspace || !ConfirmUnsavedFlowChanges("切换权限"))
            {
                OnPropertyChanged();
                return;
            }

            _selectedUserRole = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemAdministrator));
            OnPropertyChanged(nameof(CanManageProjects));
            OnPropertyChanged(nameof(RoleDescription));
            CloseProjectEditor();
            CloseNewFlowProjectDialog();
            CloseAddFlowStepDialog();
            if (!IsSystemAdministrator)
            {
                SwitchWorkspace(true, SelectedProject, confirmChanges: false);
                SelectedShellIndex = 6;
            }
            RefreshWorkspaceState();
            RaiseCommandStates();
            SaveAppSettings();
            AppendLog($"当前权限已切换为{value}。");
        }
    }

    public string DriverFileDisplay => FilePathDisplayFormatter.GetFileName(DriverFilePath);
    public string ApplicationFileDisplay => FilePathDisplayFormatter.GetFileName(ApplicationFilePath);

    public bool CanAccessPage(int pageIndex, bool projectWorkspace) => projectWorkspace
        ? pageIndex is ProjectFirmwareFlashPageIndex or ProjectFlashFlowPageIndex
        : pageIndex is >= 1 and <= 6 && (IsSystemAdministrator || pageIndex is 2 or 6);

    public bool NavigateToProjectPage(ProjectConfigEntry? project, int pageIndex)
    {
        if (!CanChangeWorkspace || project is null || !Projects.Contains(project)
            || !CanAccessPage(pageIndex, projectWorkspace: true)
            || !SwitchWorkspace(true, project))
        {
            SyncNavigationSelection();
            return false;
        }

        SelectedShellIndex = pageIndex;
        ExpandActiveProjectNavigation();
        return true;
    }

    public bool NavigateToUdsPage(int pageIndex)
    {
        if (!CanAccessPage(pageIndex, projectWorkspace: false)
            || pageIndex != 6 && !CanChangeWorkspace
            || pageIndex != 6 && !SwitchWorkspace(false, SelectedProject))
        {
            SyncNavigationSelection();
            return false;
        }
        SelectedShellIndex = pageIndex;
        return true;
    }

    private bool SwitchWorkspace(bool projectWorkspace, ProjectConfigEntry? project, bool confirmChanges = true)
    {
        if (projectWorkspace == IsProjectWorkspace && (!projectWorkspace || ReferenceEquals(project, SelectedProject)))
        {
            return true;
        }
        if (!CanChangeWorkspace
            || confirmChanges && !ConfirmUnsavedFlowChanges("切换刷写工作区")
            || projectWorkspace && project is not null && !Projects.Contains(project))
        {
            return false;
        }

        CloseProjectEditor();
        ResetFlashProgress();
        _isProjectWorkspace = projectWorkspace;
        _suppressFlowSelectionPrompt = true;
        try
        {
            if (projectWorkspace && !ReferenceEquals(project, SelectedProject))
            {
                SelectedProject = project;
            }
            else
            {
                ApplySelectedProject();
                if (IsFrameFilterEnabled)
                {
                    Frames.Clear();
                }
            }
        }
        finally
        {
            _suppressFlowSelectionPrompt = false;
        }
        RefreshWorkspaceState();
        RaiseCommandStates();
        return true;
    }

    private void InitializeNavigation()
    {
        OpenActiveProjectConfigCommand = AdministratorCommand(
            OpenActiveProjectConfig, () => SelectedProject is not null);
        ProjectNavigationView = new ListCollectionView(ProjectNavigationItems);
        Projects.CollectionChanged += (_, _) => RefreshProjectNavigation();
        (string Title, FluentIconKind Icon, int Index)[] pages =
        [
            ("开发者选项", FluentIconKind.Console, 1),
            ("操作日志", FluentIconKind.TextBoxOutline, 2),
            ("项目配置", FluentIconKind.FolderCogOutline, 3),
            ("流程配置", FluentIconKind.FileTreeOutline, 4),
            ("算法配置", FluentIconKind.FunctionVariant, 5),
            ("系统设置", FluentIconKind.CogOutline, 6)
        ];
        foreach (var page in pages)
        {
            UdsNavigationItems.Add(new(page.Title, page.Icon, page.Index, null,
                () => NavigateToUdsPage(page.Index),
                () => CanAccessPage(page.Index, false) && (page.Index == 6 || CanChangeWorkspace)));
        }
        SyncNavigationSelection();
    }

    private void RefreshProjectNavigation()
    {
        foreach (var removed in ProjectNavigationItems.Where(item => !Projects.Contains(item.Project)).ToList())
        {
            ProjectNavigationItems.Remove(removed);
        }
        for (var index = 0; index < Projects.Count; index++)
        {
            var project = Projects[index];
            var group = ProjectNavigationItems.FirstOrDefault(item => ReferenceEquals(item.Project, project));
            if (group is null)
            {
                group = new(
                    project,
                    CreateProjectNavigationPage(project, "固件刷写", FluentIconKind.FlashOutline, ProjectFirmwareFlashPageIndex),
                    CreateProjectNavigationPage(project, "刷写流程", FluentIconKind.FileTreeOutline, ProjectFlashFlowPageIndex));
                ProjectNavigationItems.Insert(index, group);
            }
            else if (ProjectNavigationItems.IndexOf(group) != index)
            {
                ProjectNavigationItems.Move(ProjectNavigationItems.IndexOf(group), index);
            }
        }
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasNoProjects));
        SyncNavigationSelection();
    }

    private ShellNavigationItemViewModel CreateProjectNavigationPage(
        ProjectConfigEntry project, string title, FluentIconKind icon, int pageIndex) =>
        new(title, icon, pageIndex, project,
            () => NavigateToProjectPage(project, pageIndex),
            () => CanChangeWorkspace && CanAccessPage(pageIndex, projectWorkspace: true));

    private void OpenActiveProjectConfig() => NavigateToUdsPage(3);

    private void ExpandActiveProjectNavigation()
    {
        if (IsProjectWorkspace && ProjectNavigationItems.FirstOrDefault(
                group => ReferenceEquals(group.Project, SelectedProject)) is { } activeGroup)
        {
            activeGroup.IsExpanded = true;
        }
    }

    private void SyncNavigationSelection()
    {
        foreach (var group in ProjectNavigationItems)
        {
            var selected = IsProjectWorkspace && ReferenceEquals(group.Project, SelectedProject);

            foreach (var page in group.Pages)
            {
                page.IsSelected = selected && page.PageIndex == SelectedShellIndex;
            }
        }
        foreach (var page in UdsNavigationItems)
        {
            page.IsVisible = CanAccessPage(page.PageIndex, false);
            page.IsSelected = (IsUdsWorkspace || page.PageIndex == 6) && page.PageIndex == SelectedShellIndex;
        }
        OnPropertyChanged(nameof(VisibleToolCount));
        RefreshNavigationCommands();
    }

    private void RefreshNavigationCommands()
    {
        foreach (var page in ProjectNavigationItems.SelectMany(group => group.Pages).Concat(UdsNavigationItems))
        {
            page.NavigateCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshWorkspaceState()
    {
        OnPropertyChanged(nameof(IsProjectWorkspace));
        OnPropertyChanged(nameof(IsUdsWorkspace));
        OnPropertyChanged(nameof(WorkspaceDescription));
        OnPropertyChanged(nameof(HasSelectedProject));
        OnPropertyChanged(nameof(FlashCommunicationSummaryText));
        OnPropertyChanged(nameof(StatusBarProjectText));
        OnPropertyChanged(nameof(FlashPageProjectIdText));
        OnPropertyChanged(nameof(CanManageProjects));
        OnPropertyChanged(nameof(IsCanFdRequested));
        RaiseConnectionDetailProperties();
        ApplyLogFilters();
        SyncNavigationSelection();
    }

    private bool LogEntryMatchesWorkspace(SystemLogEntry entry) => !IsProjectWorkspace
        || SelectedProject is not null
        && string.Equals(entry.ProjectName, SelectedProject.ProjectName, StringComparison.OrdinalIgnoreCase);

    private RelayCommand AdministratorCommand(Action execute, Func<bool>? canExecute = null) =>
        new(execute, () => IsSystemAdministrator && CanChangeWorkspace && (canExecute?.Invoke() ?? true));

    private RelayCommand FlowEditorCommand(Action execute, Func<bool>? canExecute = null) =>
        new(execute, () => CanEditFlow && (canExecute?.Invoke() ?? true));
}
