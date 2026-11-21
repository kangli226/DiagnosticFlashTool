using System.Collections.ObjectModel;
using DiagnosticFlashTool.App.Utilities;
using DiagnosticFlashTool.App.Views.Controls;
using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string StandardRoleText = "普通用户";
    private const string AdministratorRoleText = "系统管理员";
    private string _selectedUserRole = StandardRoleText;
    private bool _isProjectWorkspace = true;

    public IReadOnlyList<string> UserRoles { get; } = [StandardRoleText, AdministratorRoleText];
    public ObservableCollection<ProjectNavigationGroupViewModel> ProjectNavigationItems { get; } = [];
    public ObservableCollection<ShellNavigationItemViewModel> UdsNavigationItems { get; } = [];
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

    public string WorkspaceDescription => IsProjectWorkspace
        ? SelectedProject is null ? "请选择或新增一个项目" : $"项目：{SelectedProject.ProjectName}"
        : SelectedProject is null ? "通用 UDS（未选择项目）" : $"通用 UDS / 项目：{SelectedProject.ProjectName}";

    public string FlashCommunicationSummaryText => ActiveFlashConfiguration is not { } configuration
        ? "请先选择项目"
        : $"物理请求 {configuration.PhysicalRequestId} / 功能请求 {configuration.FunctionalRequestId} / 响应 {configuration.ResponseAddressId} / {configuration.CanProtocol} / {configuration.BaudRate}";

    public string RoleDescription => IsSystemAdministrator
        ? "可查看全部项目、通用 UDS 工具、全部项目配置、开发者选项、流程配置、算法配置和系统设置。"
        : "可使用各项目的固件刷写、通用操作日志，以及系统设置。";

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

    public bool CanAccessPage(int pageIndex, bool projectWorkspace) => pageIndex is >= 0 and <= 6
        && (projectWorkspace || pageIndex != 0)
        && (IsSystemAdministrator
            || pageIndex == 6
            || (projectWorkspace ? pageIndex == 0 : pageIndex == 2));

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

        // 从项目节点进入「项目配置」时直接打开该项目的编辑区，而不是停在全部项目列表。
        if (pageIndex == 3)
        {
            EditSelectedProject();
        }

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
        if (!CanChangeWorkspace || confirmChanges && !ConfirmUnsavedFlowChanges("切换刷写工作区"))
        {
            return false;
        }

        CloseProjectEditor();
        Progress = 0;
        DownloadStatusKind = DiagnosticStatusKind.Neutral;
        _isProjectWorkspace = projectWorkspace;
        _suppressFlowSelectionPrompt = true;
        try
        {
            if (projectWorkspace)
            {
                SelectedProject = project;
            }
            ApplyActiveFlashConfiguration();
        }
        finally
        {
            _suppressFlowSelectionPrompt = false;
        }
        RefreshWorkspaceState();
        RaiseCommandStates();
        return true;
    }

    private void ApplyActiveFlashConfiguration()
    {
        ApplySelectedProject();
        // 相同 BOOT 文件也可能用于不同项目，仍需恢复其流程和过滤状态。
        LoadFlowFromSelected();
        if (IsFrameFilterEnabled)
        {
            Frames.Clear();
        }
    }

    private void InitializeNavigation()
    {
        OpenActiveProjectConfigCommand = AdministratorCommand(
            OpenActiveProjectConfig, () => SelectedProject is not null);
        Projects.CollectionChanged += (_, _) => RefreshProjectNavigation();
        (string Title, FluentIconKind Icon, int Index)[] pages =
        [
            ("开发者选项", FluentIconKind.Console, 1),
            ("操作日志", FluentIconKind.TextBoxOutline, 2),
            ("全部项目配置", FluentIconKind.FolderCogOutline, 3),
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
                var pages = new[]
                {
                    CreateProjectNavigationPage(project, "固件刷写", FluentIconKind.FlashOutline, 0),
                    CreateProjectNavigationPage(project, "刷写流程", FluentIconKind.FileTreeOutline, 4),
                    CreateProjectNavigationPage(project, "项目配置", FluentIconKind.FolderCogOutline, 3)
                };
                group = new(project, pages);
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

    private void OpenActiveProjectConfig()
    {
        if (SelectedProject is null || !NavigateToUdsPage(3))
        {
            return;
        }
        EditSelectedProject();
    }

    private void SyncNavigationSelection()
    {
        foreach (var group in ProjectNavigationItems)
        {
            var selected = IsProjectWorkspace && ReferenceEquals(group.Project, SelectedProject);

            // 项目不再折叠：只展开当前选中的项目，避免所有项目的页面同时铺开。
            group.IsExpanded = selected;

            foreach (var page in group.Pages)
            {
                // 页面条目跟随所属项目是否展开，未展开的项目只保留项目名一行。
                page.IsVisible = selected && CanAccessPage(page.PageIndex, projectWorkspace: true);
                page.IsSelected = selected && page.PageIndex == SelectedShellIndex;
            }
        }
        foreach (var page in UdsNavigationItems)
        {
            page.IsVisible = CanAccessPage(page.PageIndex, false);
            page.IsSelected = (IsUdsWorkspace || page.PageIndex == 6) && page.PageIndex == SelectedShellIndex;
        }
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
        || SelectedProject is not null && entry.ProjectNo == SelectedProject.ProjectNo;

    private RelayCommand AdministratorCommand(Action execute, Func<bool>? canExecute = null) =>
        new(execute, () => IsSystemAdministrator && CanChangeWorkspace && (canExecute?.Invoke() ?? true));
}
