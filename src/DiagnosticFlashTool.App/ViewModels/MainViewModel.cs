using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DiagnosticFlashTool.Core.Algorithms;
using DiagnosticFlashTool.Core.Can;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Firmware;
using DiagnosticFlashTool.Core.Flashing;
using DiagnosticFlashTool.Core.Util;
using DiagnosticFlashTool.Infrastructure.Can;
using DiagnosticFlashTool.Infrastructure.Configuration;
using Microsoft.Win32;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string AppSettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiagnosticFlashTool",
        "settings.json");
    private static readonly string DefaultLogFilePath = Path.Combine("logs", "DiagnosticFlashTool.log");
    private const int DefaultLogRetentionDays = 30;
    private const int DefaultLogMaxFileSizeMb = 10;
    private const int MinimumLogRetentionDays = 1;
    private const int MaximumLogRetentionDays = 3650;
    private const int MinimumLogMaxFileSizeMb = 1;
    private const int MaximumLogMaxFileSizeMb = 1024;
    private const int MaxLogEntries = 1000;
    private const int MaxRecentLogEntries = 10;
    private const int MaxDownloadInfoEntries = 30;
    private const int MaxFrameHistoryEntries = 5000;

    private readonly AppConfigurationPaths _paths = new();
    private readonly JsonProjectConfigRepository _projectRepository;
    private readonly JsonBootConfigRepository _bootConfigRepository;
    private readonly FirmwareLoader _firmwareLoader = new();
    private readonly CanDeviceFactory _canDeviceFactory = new();

    /// <summary>
    /// Algorithms the flash executor may resolve. Matches what the flow editor validates
    /// against, so the editor cannot accept a flow the executor would refuse.
    /// </summary>
    private readonly SeedKeyAlgorithmRegistry _seedKeyAlgorithmRegistry = new();
    private ICanDevice? _canDevice;
    private BootConfig? _loadedFlowConfig;
    private BootConfig? _draftFlowConfig;
    private ProjectConfigEntry? _selectedProject;
    private ProjectConfigEntry? _draftFlowProject;
    private string? _draftFlowTemplateSnapshotJson;
    private string? _flowBaselineFingerprint;
    private ProjectConfigEntry? _editingProject;
    private FlowStepEditorRow? _selectedFlowStep;
    private FlowStepTemplate? _selectedFlowStepTemplate;
    private string? _selectedBootConfig;
    private string _selectedDeviceType = "Mock";
    private string _selectedBaudRate = "500K";
    private string _driverFilePath = string.Empty;
    private string _applicationFilePath = string.Empty;
    private string _manualFrameId = "7E0";
    private string _manualFrameData = "02 10 03";
    private string _selectedCanChannel = "0";
    private string _logFilePath = DefaultLogFilePath;
    private string _selectedLogLevelFilter = "全部";
    private string _selectedLogTimeRangeFilter = "全部";
    private string _logSearchText = string.Empty;
    private bool _keepRunningInTray;
    private bool _autoFlashEnabled;
    private bool _autoSearchBaudRate = true;
    private bool _logFileEnabled = true;
    private bool _logAutoScrollEnabled = true;
    private bool _adminModeEnabled;
    private bool _adminPasswordPromptVisible;
    private bool _isFrameCapturePaused;
    private bool _isFrameFilterEnabled;
    private bool _isProjectEditorVisible;
    private bool _isNewFlowProjectDialogVisible;
    private bool _isAddFlowStepDialogVisible;
    private bool _isReplacingFlowStepTemplate;
    private bool _suppressFlowSelectionPrompt;
    private bool _createFlowProjectFromTemplate = true;
    private bool _isConnected;
    private bool _isBusy;
    private bool _disposed;
    private int _selectedShellIndex;
    private int _progress;
    private int _logRetentionDays = DefaultLogRetentionDays;
    private int _logMaxFileSizeMb = DefaultLogMaxFileSizeMb;
    private string _statusText = "Ready";
    private DiagnosticStatusKind _statusKind = DiagnosticStatusKind.Neutral;
    private DiagnosticStatusKind _downloadStatusKind = DiagnosticStatusKind.Neutral;
    private string _flowValidationText = "未加载流程配置。";
    private string _projectEditorTitle = "新建项目";
    private string _newFlowProjectName = string.Empty;
    private string? _selectedFlowProjectTemplate;
    private string _newFlowProjectValidationText = string.Empty;
    private string _adminPassword = string.Empty;
    private string _adminPasswordMessage = "请输入密码，本次启动内有效。";
    private SystemLogEntry? _latestLogEntry;
    private readonly List<CanFrameRow> _frameHistory = [];

    public MainViewModel()
    {
        CommandErrorHandler.Current = HandleCommandException;
        LoadAppSettings();
        _projectRepository = new JsonProjectConfigRepository(_paths);
        _bootConfigRepository = new JsonBootConfigRepository(_paths);

        RefreshCommand = new RelayCommand(RefreshWithConfirmation);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected && !IsBusy);
        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => !IsBusy);
        BrowseDriverCommand = new RelayCommand(() => BrowseFirmware(path => DriverFilePath = path));
        BrowseApplicationCommand = new RelayCommand(() => BrowseFirmware(path => ApplicationFilePath = path));
        StartFlashCommand = new AsyncRelayCommand(
            StartFlashAsync,
            () => IsConnected
                && SelectedProject is not null
                && !ReferenceEquals(SelectedProject, _draftFlowProject)
                && !IsBusy);
        CancelFlashCommand = new RelayCommand(() => StartFlashCommand.Cancel(), () => StartFlashCommand.IsRunning);
        RefreshLogCommand = new RelayCommand(ApplyLogFilters);
        ClearLogCommand = new RelayCommand(ClearLogs);
        OpenLogFileCommand = new RelayCommand(OpenLogFile);
        ExportLogCommand = new RelayCommand(ExportLog);
        ClearFramesCommand = new RelayCommand(ClearFrames);
        ToggleFrameCaptureCommand = new RelayCommand(ToggleFrameCapture);
        ToggleFrameFilterCommand = new RelayCommand(ToggleFrameFilter);
        ExportFramesCommand = new RelayCommand(ExportFrames);
        AddProjectCommand = new RelayCommand(AddProject, () => _draftFlowProject is null);
        DeleteProjectCommand = new RelayCommand(
            DeleteSelectedProject,
            () => _draftFlowProject is null && SelectedProject is not null);
        SaveProjectsCommand = new RelayCommand(SaveProjects, () => _draftFlowProject is null);
        RefreshProjectsCommand = new RelayCommand(RefreshProjectList);
        NewProjectCommand = new RelayCommand(OpenNewProjectEditor, () => _draftFlowProject is null);
        EditProjectCommand = new RelayCommand(
            EditSelectedProject,
            () => _draftFlowProject is null && SelectedProject is not null);
        SaveProjectEditorCommand = new RelayCommand(
            SaveProjectEditor,
            () => _draftFlowProject is null && IsProjectEditorVisible);
        CancelProjectEditorCommand = new RelayCommand(CloseProjectEditor, () => IsProjectEditorVisible);
        DeleteProjectEditorCommand = new RelayCommand(
            DeleteProjectEditor,
            () => _draftFlowProject is null && IsEditingProject);
        BrowseProjectEditorDriverCommand = new RelayCommand(() => BrowseFirmware(path => ProjectEditor.DriverFilePath = path));
        BrowseProjectEditorApplicationCommand = new RelayCommand(() => BrowseFirmware(path => ProjectEditor.ApplicationFilePath = path));
        LoadFlowCommand = new RelayCommand(
            ReloadFlowFromSelected,
            () => !string.IsNullOrWhiteSpace(SelectedBootConfig)
                && !ReferenceEquals(SelectedProject, _draftFlowProject));
        SaveFlowCommand = new RelayCommand(SaveFlowConfig, () => !string.IsNullOrWhiteSpace(SelectedBootConfig) && FlowRows.Count > 0);
        ValidateFlowCommand = new RelayCommand(() => ValidateFlowConfig());
        NewFlowCommand = new RelayCommand(OpenNewFlowProjectDialog);
        ConfirmNewFlowProjectCommand = new RelayCommand(
            ConfirmNewFlowProject,
            () => IsNewFlowProjectDialogVisible);
        CancelNewFlowProjectCommand = new RelayCommand(
            CloseNewFlowProjectDialog,
            () => IsNewFlowProjectDialogVisible);
        AddFlowStepCommand = new RelayCommand(OpenAddFlowStepDialog);
        ReplaceFlowStepTemplateCommand = new RelayCommand(
            OpenReplaceFlowStepDialog,
            () => SelectedFlowStep is not null);
        ConfirmAddFlowStepCommand = new RelayCommand(
            ConfirmSelectedFlowStepTemplate,
            () => IsAddFlowStepDialogVisible
                && SelectedFlowStepTemplate is not null
                && (!IsReplacingFlowStepTemplate || SelectedFlowStep is not null));
        CancelAddFlowStepCommand = new RelayCommand(
            CloseAddFlowStepDialog,
            () => IsAddFlowStepDialogVisible);
        DeleteFlowStepCommand = new RelayCommand(DeleteSelectedFlowStep, () => SelectedFlowStep is not null);
        MoveFlowStepUpCommand = new RelayCommand(() => MoveSelectedFlowStep(-1), () => SelectedFlowStep is not null);
        MoveFlowStepDownCommand = new RelayCommand(() => MoveSelectedFlowStep(1), () => SelectedFlowStep is not null);
        SendManualFrameCommand = new AsyncRelayCommand(SendManualFrameAsync, () => IsConnected && !IsBusy);
        ImportBuiltinConfigurationCommand = new RelayCommand(ImportBuiltinConfiguration);
        ClearRuntimeConfigurationCommand = new RelayCommand(ClearRuntimeConfiguration);
        OpenFlowConfigDirectoryCommand = new RelayCommand(() => OpenPathLocation(FlowConfigDirectory));
        ChooseFlowConfigDirectoryCommand = new RelayCommand(ChooseFlowConfigDirectory);
        OpenFormulaDatabaseDirectoryCommand = new RelayCommand(() => OpenPathLocation(FormulaDatabaseDirectory));
        ChooseFormulaDatabaseDirectoryCommand = new RelayCommand(ChooseFormulaDatabaseDirectory);
        OpenProjectConfigDirectoryCommand = new RelayCommand(() => OpenPathLocation(ProjectConfigPath));
        ChooseProjectConfigFileCommand = new RelayCommand(ChooseProjectConfigFile);
        OpenLogDirectoryCommand = new RelayCommand(() => OpenPathLocation(LogFilePath));
        ChooseLogFileCommand = new RelayCommand(ChooseLogFile);
        RestoreDefaultLogStorageCommand = new RelayCommand(RestoreDefaultLogStorage);
        OpenRuntimeLogPageCommand = new RelayCommand(() => SelectedShellIndex = 7);
        ToggleAdminModeCommand = new RelayCommand(ToggleAdminMode);
        SubmitAdminPasswordCommand = new RelayCommand(SubmitAdminPassword);
        CancelAdminPasswordCommand = new RelayCommand(CancelAdminPasswordPrompt);

        Refresh();
    }

    public ObservableCollection<ProjectConfigEntry> Projects { get; } = [];
    public ProjectEditorViewModel ProjectEditor { get; } = new();
    public ObservableCollection<string> BootConfigFiles { get; } = [];
    public ObservableCollection<string> DeviceTypes { get; } = ["Mock", "ZLG USBCAN-2A (4)"];
    public ObservableCollection<string> BaudRates { get; } = ["250K", "500K", "1000K"];
    public ObservableCollection<string> CanChannels { get; } = ["0", "1"];
    public ObservableCollection<string> LogLevelFilters { get; } = ["全部", "普通", "成功", "运行", "警告", "错误"];
    public ObservableCollection<string> LogTimeRangeFilters { get; } = ["全部", "最近1小时", "今天", "最近24小时", "最近7天"];
    public ObservableCollection<SystemLogEntry> LogEntries { get; } = [];
    public ObservableCollection<SystemLogEntry> FilteredLogEntries { get; } = [];
    public ObservableCollection<SystemLogEntry> RecentLogEntries { get; } = [];
    public ObservableCollection<CanFrameRow> Frames { get; } = [];
    public ObservableCollection<FlowStepEditorRow> FlowRows { get; } = [];
    public IReadOnlyList<FlowStepTemplate> FlowStepTemplates { get; } = FlowStepTemplate.BuiltIn;
    public ObservableCollection<string> FlowSecurityAlgorithmOptions { get; } = [string.Empty, "AES128_OneFunc"];
    public ObservableCollection<AlgorithmConfigRow> AlgorithmRows { get; } = [];

    public RelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand ToggleConnectionCommand { get; }
    public RelayCommand BrowseDriverCommand { get; }
    public RelayCommand BrowseApplicationCommand { get; }
    public AsyncRelayCommand StartFlashCommand { get; }
    public RelayCommand CancelFlashCommand { get; }
    public RelayCommand RefreshLogCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand OpenLogFileCommand { get; }
    public RelayCommand ExportLogCommand { get; }
    public RelayCommand ClearFramesCommand { get; }
    public RelayCommand ToggleFrameCaptureCommand { get; }
    public RelayCommand ToggleFrameFilterCommand { get; }
    public RelayCommand ExportFramesCommand { get; }
    public RelayCommand AddProjectCommand { get; }
    public RelayCommand DeleteProjectCommand { get; }
    public RelayCommand SaveProjectsCommand { get; }
    public RelayCommand RefreshProjectsCommand { get; }
    public RelayCommand NewProjectCommand { get; }
    public RelayCommand EditProjectCommand { get; }
    public RelayCommand SaveProjectEditorCommand { get; }
    public RelayCommand CancelProjectEditorCommand { get; }
    public RelayCommand DeleteProjectEditorCommand { get; }
    public RelayCommand BrowseProjectEditorDriverCommand { get; }
    public RelayCommand BrowseProjectEditorApplicationCommand { get; }
    public RelayCommand LoadFlowCommand { get; }
    public RelayCommand SaveFlowCommand { get; }
    public RelayCommand ValidateFlowCommand { get; }
    public RelayCommand NewFlowCommand { get; }
    public RelayCommand ConfirmNewFlowProjectCommand { get; }
    public RelayCommand CancelNewFlowProjectCommand { get; }
    public RelayCommand AddFlowStepCommand { get; }
    public RelayCommand ReplaceFlowStepTemplateCommand { get; }
    public RelayCommand ConfirmAddFlowStepCommand { get; }
    public RelayCommand CancelAddFlowStepCommand { get; }
    public RelayCommand DeleteFlowStepCommand { get; }
    public RelayCommand MoveFlowStepUpCommand { get; }
    public RelayCommand MoveFlowStepDownCommand { get; }
    public AsyncRelayCommand SendManualFrameCommand { get; }
    public RelayCommand ImportBuiltinConfigurationCommand { get; }
    public RelayCommand ClearRuntimeConfigurationCommand { get; }
    public RelayCommand OpenFlowConfigDirectoryCommand { get; }
    public RelayCommand ChooseFlowConfigDirectoryCommand { get; }
    public RelayCommand OpenFormulaDatabaseDirectoryCommand { get; }
    public RelayCommand ChooseFormulaDatabaseDirectoryCommand { get; }
    public RelayCommand OpenProjectConfigDirectoryCommand { get; }
    public RelayCommand ChooseProjectConfigFileCommand { get; }
    public RelayCommand OpenLogDirectoryCommand { get; }
    public RelayCommand ChooseLogFileCommand { get; }
    public RelayCommand RestoreDefaultLogStorageCommand { get; }
    public RelayCommand OpenRuntimeLogPageCommand { get; }
    public RelayCommand ToggleAdminModeCommand { get; }
    public RelayCommand SubmitAdminPasswordCommand { get; }
    public RelayCommand CancelAdminPasswordCommand { get; }

    public bool IsProjectEditorVisible
    {
        get => _isProjectEditorVisible;
        private set
        {
            if (SetProperty(ref _isProjectEditorVisible, value))
            {
                RaiseProjectEditorCommandStates();
            }
        }
    }

    public bool IsEditingProject => _editingProject is not null;

    public bool IsNewFlowProjectDialogVisible
    {
        get => _isNewFlowProjectDialogVisible;
        private set
        {
            if (SetProperty(ref _isNewFlowProjectDialogVisible, value))
            {
                ConfirmNewFlowProjectCommand.RaiseCanExecuteChanged();
                CancelNewFlowProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewFlowProjectName
    {
        get => _newFlowProjectName;
        set
        {
            if (SetProperty(ref _newFlowProjectName, value))
            {
                NewFlowProjectValidationText = string.Empty;
            }
        }
    }

    public bool CreateFlowProjectFromTemplate
    {
        get => _createFlowProjectFromTemplate;
        set
        {
            if (SetProperty(ref _createFlowProjectFromTemplate, value))
            {
                OnPropertyChanged(nameof(CreateBlankFlowProject));
                NewFlowProjectValidationText = string.Empty;
            }
        }
    }

    public bool CreateBlankFlowProject
    {
        get => !CreateFlowProjectFromTemplate;
        set
        {
            if (value)
            {
                CreateFlowProjectFromTemplate = false;
            }
        }
    }

    public string? SelectedFlowProjectTemplate
    {
        get => _selectedFlowProjectTemplate;
        set
        {
            if (SetProperty(ref _selectedFlowProjectTemplate, value))
            {
                NewFlowProjectValidationText = string.Empty;
            }
        }
    }

    public string NewFlowProjectValidationText
    {
        get => _newFlowProjectValidationText;
        private set => SetProperty(ref _newFlowProjectValidationText, value);
    }

    public bool HasBootConfigTemplates => BootConfigFiles.Count > 0;

    public bool IsAddFlowStepDialogVisible
    {
        get => _isAddFlowStepDialogVisible;
        private set
        {
            if (SetProperty(ref _isAddFlowStepDialogVisible, value))
            {
                RaiseAddFlowStepCommandStates();
            }
        }
    }

    public bool IsReplacingFlowStepTemplate => _isReplacingFlowStepTemplate;

    public string FlowStepTemplateDialogTitle => IsReplacingFlowStepTemplate
        ? "更换节点模板"
        : "添加流程节点";

    public string FlowStepTemplateDialogDescription => IsReplacingFlowStepTemplate
        ? "替换模板固定字段，并保留兼容的下载块大小或安全算法参数"
        : "从内置模板添加节点，固定协议字段不可自定义";

    public string FlowStepTemplateDialogConfirmText => IsReplacingFlowStepTemplate ? "更换" : "添加";

    public string ProjectEditorTitle
    {
        get => _projectEditorTitle;
        private set => SetProperty(ref _projectEditorTitle, value);
    }

    public ProjectConfigEntry? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (ReferenceEquals(_selectedProject, value))
            {
                return;
            }

            if (!_suppressFlowSelectionPrompt
                && HasUnsavedFlowChanges()
                && !ConfirmUnsavedFlowChanges("切换配置", selectFallbackWhenDiscardingDraft: false))
            {
                OnPropertyChanged(nameof(SelectedProject));
                return;
            }

            if (SetProperty(ref _selectedProject, value))
            {
                ApplySelectedProject();
                OnPropertyChanged(nameof(StatusBarProjectText));
                RaiseWorkbenchContextText();
                if (IsFrameFilterEnabled)
                {
                    Frames.Clear();
                }

                RaiseCommandStates();
            }
        }
    }

    public FlowStepEditorRow? SelectedFlowStep
    {
        get => _selectedFlowStep;
        set
        {
            if (SetProperty(ref _selectedFlowStep, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public FlowStepTemplate? SelectedFlowStepTemplate
    {
        get => _selectedFlowStepTemplate;
        set
        {
            if (SetProperty(ref _selectedFlowStepTemplate, value))
            {
                ConfirmAddFlowStepCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedBootConfig
    {
        get => _selectedBootConfig;
        private set
        {
            if (SetProperty(ref _selectedBootConfig, value))
            {
                OnPropertyChanged(nameof(FlowConfigFilePath));
                OnPropertyChanged(nameof(StatusBarProjectText));
                RaiseWorkbenchContextText();
                LoadFlowFromSelected();
                RaiseCommandStates();
            }
        }
    }

    public string SelectedDeviceType
    {
        get => _selectedDeviceType;
        set
        {
            if (SetProperty(ref _selectedDeviceType, value))
            {
                SaveAppSettings();
                OnPropertyChanged(nameof(ConnectionText));
                OnPropertyChanged(nameof(ConnectionSummaryText));
                OnPropertyChanged(nameof(StatusBarDeviceText));
            }
        }
    }

    public string SelectedBaudRate
    {
        get => _selectedBaudRate;
        set
        {
            if (SetProperty(ref _selectedBaudRate, value))
            {
                SaveAppSettings();
                OnPropertyChanged(nameof(ConnectionText));
                OnPropertyChanged(nameof(ConnectionSummaryText));
                OnPropertyChanged(nameof(BaudRateStatusText));
                OnPropertyChanged(nameof(StatusBarDeviceText));
            }
        }
    }

    public string DriverFilePath
    {
        get => _driverFilePath;
        set => SetProperty(ref _driverFilePath, value);
    }

    public string ApplicationFilePath
    {
        get => _applicationFilePath;
        set => SetProperty(ref _applicationFilePath, value);
    }

    public string ManualFrameId
    {
        get => _manualFrameId;
        set => SetProperty(ref _manualFrameId, value);
    }

    public string ManualFrameData
    {
        get => _manualFrameData;
        set => SetProperty(ref _manualFrameData, value);
    }

    public string SelectedCanChannel
    {
        get => _selectedCanChannel;
        set
        {
            if (SetProperty(ref _selectedCanChannel, value))
            {
                SaveAppSettings();
                OnPropertyChanged(nameof(StatusBarDeviceText));
            }
        }
    }

    public bool KeepRunningInTray
    {
        get => _keepRunningInTray;
        set
        {
            if (SetProperty(ref _keepRunningInTray, value))
            {
                OnPropertyChanged(nameof(KeepRunningInTrayStatusText));
                SaveAppSettings();
                AppendLog($"Run in background {(value ? "enabled" : "disabled")}.");
            }
        }
    }

    public string KeepRunningInTrayStatusText => KeepRunningInTray
        ? "关闭窗口时驻留到系统托盘"
        : "关闭窗口时直接退出程序";

    public bool AutoFlashEnabled
    {
        get => _autoFlashEnabled;
        set
        {
            if (SetProperty(ref _autoFlashEnabled, value))
            {
                SaveAppSettings();
            }
        }
    }

    public bool AutoSearchBaudRate
    {
        get => _autoSearchBaudRate;
        set
        {
            if (SetProperty(ref _autoSearchBaudRate, value))
            {
                SaveAppSettings();
            }
        }
    }

    public bool LogFileEnabled
    {
        get => _logFileEnabled;
        set
        {
            if (SetProperty(ref _logFileEnabled, value))
            {
                OnPropertyChanged(nameof(LogFileStatusText));
                OnPropertyChanged(nameof(LogFileStatusKind));
                SaveAppSettings();
            }
        }
    }

    public string LogFilePath
    {
        get => _logFilePath;
        set
        {
            if (SetProperty(ref _logFilePath, value))
            {
                OnPropertyChanged(nameof(LogFileStatusPathText));
                SaveAppSettings();
            }
        }
    }

    public string LogFileStatusText => LogFileEnabled ? "文件日志：已开启" : "文件日志：未开启";

    public DiagnosticStatusKind LogFileStatusKind => LogFileEnabled ? DiagnosticStatusKind.Success : DiagnosticStatusKind.Neutral;

    public string LogFileStatusPathText => LogFilePath;

    public string SelectedLogLevelFilter
    {
        get => _selectedLogLevelFilter;
        set
        {
            if (SetProperty(ref _selectedLogLevelFilter, value))
            {
                ApplyLogFilters();
            }
        }
    }

    public string LogSearchText
    {
        get => _logSearchText;
        set
        {
            if (SetProperty(ref _logSearchText, value))
            {
                ApplyLogFilters();
            }
        }
    }

    public string SelectedLogTimeRangeFilter
    {
        get => _selectedLogTimeRangeFilter;
        set
        {
            if (SetProperty(ref _selectedLogTimeRangeFilter, value))
            {
                ApplyLogFilters();
            }
        }
    }

    public bool LogAutoScrollEnabled
    {
        get => _logAutoScrollEnabled;
        set
        {
            if (SetProperty(ref _logAutoScrollEnabled, value))
            {
                SaveAppSettings();
            }
        }
    }

    public string LogFilterSummaryText => $"显示 {FilteredLogEntries.Count} / {LogEntries.Count} 条";

    public bool IsFrameCapturePaused
    {
        get => _isFrameCapturePaused;
        private set
        {
            if (SetProperty(ref _isFrameCapturePaused, value))
            {
                OnPropertyChanged(nameof(FrameCaptureActionText));
            }
        }
    }

    public bool IsFrameFilterEnabled
    {
        get => _isFrameFilterEnabled;
        private set
        {
            if (SetProperty(ref _isFrameFilterEnabled, value))
            {
                OnPropertyChanged(nameof(FrameFilterActionText));
            }
        }
    }

    public string FrameCaptureActionText => IsFrameCapturePaused ? "继续" : "暂停";
    public string FrameFilterActionText => IsFrameFilterEnabled ? "过滤开启" : "过滤关闭";

    public string DownloadInfoText => LogEntries.Count == 0
        ? "暂无下载信息"
        : string.Join(Environment.NewLine, LogEntries.TakeLast(MaxDownloadInfoEntries).Select(entry => entry.Text));

    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set
        {
            var normalized = NormalizeLogRetentionDays(value);
            if (SetProperty(ref _logRetentionDays, normalized))
            {
                SaveAppSettings();
            }
        }
    }

    public int LogMaxFileSizeMb
    {
        get => _logMaxFileSizeMb;
        set
        {
            var normalized = NormalizeLogMaxFileSizeMb(value);
            if (SetProperty(ref _logMaxFileSizeMb, normalized))
            {
                SaveAppSettings();
            }
        }
    }

    public bool AdminModeAvailable => true;

    public bool AdminModeEnabled
    {
        get => _adminModeEnabled;
        private set
        {
            if (SetProperty(ref _adminModeEnabled, value))
            {
                OnPropertyChanged(nameof(AdminModeStatusText));
            }
        }
    }

    public string AdminModeStatusText => AdminModeEnabled ? "已启用" : "未启用";

    public bool AdminPasswordPromptVisible
    {
        get => _adminPasswordPromptVisible;
        private set => SetProperty(ref _adminPasswordPromptVisible, value);
    }

    public string AdminPassword
    {
        get => _adminPassword;
        set => SetProperty(ref _adminPassword, value);
    }

    public string AdminPasswordMessage
    {
        get => _adminPasswordMessage;
        private set => SetProperty(ref _adminPasswordMessage, value);
    }

    public int SelectedShellIndex
    {
        get => _selectedShellIndex;
        set
        {
            if (SetProperty(ref _selectedShellIndex, value))
            {
                CloseNewFlowProjectDialog();
                CloseAddFlowStepDialog();
                OnPropertyChanged(nameof(CurrentPageContextText));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionText));
                OnPropertyChanged(nameof(ConnectionActionText));
                OnPropertyChanged(nameof(DeviceStatusText));
                OnPropertyChanged(nameof(DeviceStatusKind));
                OnPropertyChanged(nameof(DeviceStatusIcon));
                OnPropertyChanged(nameof(ConnectionSummaryText));
                OnPropertyChanged(nameof(BaudRateStatusText));
                OnPropertyChanged(nameof(StatusBarDeviceText));
                RaiseWorkbenchContextText();
                RaiseCommandStates();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(DownloadStatusText));
                OnPropertyChanged(nameof(DownloadMonitorDetailText));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetProperty(ref _statusText, value))
            {
                StatusKind = ClassifyStatusText(value);
                RaiseDeviceStatusProperties();
            }
        }
    }

    public DiagnosticStatusKind StatusKind
    {
        get => _statusKind;
        private set
        {
            if (SetProperty(ref _statusKind, value))
            {
                OnPropertyChanged(nameof(DeviceStatusText));
                OnPropertyChanged(nameof(DeviceStatusKind));
                OnPropertyChanged(nameof(DeviceStatusIcon));
                OnPropertyChanged(nameof(ConnectionSummaryText));
            }
        }
    }

    public DiagnosticStatusKind DownloadStatusKind
    {
        get => _downloadStatusKind;
        private set
        {
            if (SetProperty(ref _downloadStatusKind, value))
            {
                OnPropertyChanged(nameof(DownloadStatusText));
                OnPropertyChanged(nameof(DownloadStatusIcon));
                OnPropertyChanged(nameof(DownloadMonitorDetailText));
                OnPropertyChanged(nameof(StatusBarStateText));
            }
        }
    }

    public string FlowValidationText
    {
        get => _flowValidationText;
        set => SetProperty(ref _flowValidationText, value);
    }

    public string ConnectionText => IsConnected ? $"Connected: {SelectedDeviceType} / {SelectedBaudRate}" : "Disconnected";
    public string BaudRateStatusText => IsConnected ? SelectedBaudRate : "待自动搜索";
    public string ConnectionSummaryText => IsConnected
        ? $"{SelectedDeviceType} / {SelectedBaudRate}"
        : IsConnectionFailure ? "连接失败，请重试" : "等待设备连接";
    public string ConnectionActionText => IsConnected ? "断开设备" : "连接设备";
    public string DeviceStatusText => IsConnected
        ? "设备在线"
        : IsConnectionFailure ? "连接失败" : "未连接";
    public DiagnosticStatusKind DeviceStatusKind => IsConnected
        ? DiagnosticStatusKind.Success
        : IsConnectionFailure ? DiagnosticStatusKind.Error : DiagnosticStatusKind.Neutral;
    public string DeviceStatusIcon => DeviceStatusKind switch
    {
        DiagnosticStatusKind.Success => "\u2713",
        DiagnosticStatusKind.Error => "\u00D7",
        _ => "\u24D8"
    };
    public string DownloadStatusIcon => DownloadStatusKind switch
    {
        DiagnosticStatusKind.Success => "\u2713",
        DiagnosticStatusKind.Running => "\u25CF",
        DiagnosticStatusKind.Warning => "\u26A0",
        DiagnosticStatusKind.Error => "\u00D7",
        _ => "\u24D8"
    };
    public string DownloadStatusText => DownloadStatusKind switch
    {
        DiagnosticStatusKind.Success => "已完成",
        DiagnosticStatusKind.Running => "下载中",
        DiagnosticStatusKind.Warning => "已取消",
        DiagnosticStatusKind.Error => "刷写失败",
        _ => Progress >= 100 ? "已完成" : "未开始"
    };

    public string DownloadMonitorDetailText => DownloadStatusKind switch
    {
        DiagnosticStatusKind.Running => ProgressText,
        DiagnosticStatusKind.Success => "100%",
        DiagnosticStatusKind.Warning => "已取消",
        DiagnosticStatusKind.Error => "请检查日志",
        _ => "等待刷写任务"
    };

    public string ProgressText => $"{Progress}%";
    public string ScriptRootText => Path.Combine(_paths.ConfigDirectory, "Scripts");
    public string AlgorithmCatalogText => AlgorithmRows.Count == 0 ? "未发现算法配置" : $"已发现 {AlgorithmRows.Count} 个算法配置";

    /// <summary>工作台页头上下文：当前项目、BOOT 配置与设备连接状态。</summary>
    public string WorkbenchContextText
    {
        get
        {
            var connection = IsConnected ? "设备已连接" : "设备未连接";
            if (SelectedProject is null)
            {
                return $"未选择项目 · {connection}";
            }

            var boot = string.IsNullOrWhiteSpace(SelectedBootConfig)
                ? "未指定 BOOT"
                : $"BOOT {SelectedBootConfig}";

            return $"项目 {SelectedProject.ProjectName} · {boot} · {connection}";
        }
    }

    /// <summary>当前页面的页头上下文文案；由页头隐式样式绑定，页面 XAML 不再声明页头文案。</summary>
    public string CurrentPageContextText => SelectedShellIndex switch
    {
        0 => WorkbenchContextText,
        1 => "调试刷写流程、发送 CAN 消息、查看接收日志",
        2 => "刷写历史尚未接入持久化，结果暂请在系统日志中追溯",
        3 => "维护项目通信参数、BOOT 配置与固件文件",
        4 => "编辑 BOOT 配置中的刷写流程节点与脚本",
        5 => "查看已注册的安全访问算法与校验脚本",
        6 => "配置 CAN 适配器、运行路径、日志与管理员模式",
        7 => "查看运行日志与本地日志文件",
        _ => string.Empty
    };

    /// <summary>工作台上下文变化时同时刷新页头文案与页头上下文属性。</summary>
    private void RaiseWorkbenchContextText()
    {
        OnPropertyChanged(nameof(WorkbenchContextText));
        OnPropertyChanged(nameof(CurrentPageContextText));
    }

    /// <summary>状态栏运行态：当前 CAN 设备与通道。</summary>
    public string StatusBarDeviceText => IsConnected
        ? $"设备：{SelectedDeviceType} / {SelectedBaudRate} / 通道 {SelectedCanChannel}"
        : "设备：未连接";

    /// <summary>状态栏运行态：当前项目与 BOOT 配置。</summary>
    public string StatusBarProjectText => SelectedProject is null
        ? "项目：未选择"
        : string.IsNullOrWhiteSpace(SelectedBootConfig)
            ? $"项目：{SelectedProject.ProjectName}"
            : $"项目：{SelectedProject.ProjectName} / BOOT：{SelectedBootConfig}";

    /// <summary>状态栏运行态：当前下载/刷写状态。</summary>
    public string StatusBarStateText => $"状态：{DownloadStatusText}";

    public SystemLogEntry? LatestLogEntry
    {
        get => _latestLogEntry;
        private set => SetProperty(ref _latestLogEntry, value);
    }

    public bool HasLogEntries => LogEntries.Count > 0;
    public string LatestLogText => LatestLogEntry?.Text ?? "暂无日志";
    public string LogEntryCountText => HasLogEntries ? $"已记录 {LogEntries.Count} 条" : "暂无日志";

    public string ConfigRootText => _paths.ConfigDirectory;
    public string FlowConfigDirectory => _paths.BootConfigDirectory;
    public string FlowConfigFilePath => string.IsNullOrWhiteSpace(SelectedBootConfig)
        ? string.Empty
        : Path.IsPathRooted(SelectedBootConfig)
            ? SelectedBootConfig
            : Path.Combine(FlowConfigDirectory, SelectedBootConfig);
    public string FormulaDatabaseDirectory => _paths.FormulaDatabaseDirectory;
    public string ProjectConfigPath => _paths.ProjectConfigPath;
    public string FlowConfigDirectoryDisplay => GetCompactPathDisplay(FlowConfigDirectory);
    public string FormulaDatabaseDirectoryDisplay => GetCompactPathDisplay(FormulaDatabaseDirectory);
    public string ProjectConfigPathDisplay => GetCompactPathDisplay(ProjectConfigPath);

    private bool IsConnectionFailure => !IsConnected
        && StatusKind == DiagnosticStatusKind.Error
        && ContainsAny(StatusText, "connect failed", "connection failed", "连接失败");

    public bool EnableAdminMode(string password)
    {
        if (string.Equals(password, "admin", StringComparison.Ordinal))
        {
            AdminModeEnabled = true;
            SetStatus("管理员模式已启动", DiagnosticStatusKind.Success);
            AppendLog("Admin mode enabled.");
            return true;
        }

        SetStatus("管理员密码错误", DiagnosticStatusKind.Warning);
        AppendLog("Admin mode password rejected.");
        return false;
    }

    private void ToggleAdminMode()
    {
        if (AdminModeEnabled)
        {
            AdminModeEnabled = false;
            AdminPassword = string.Empty;
            AdminPasswordPromptVisible = false;
            SetStatus("管理员模式已退出", DiagnosticStatusKind.Success);
            AppendLog("Admin mode disabled.");
            return;
        }

        ShowAdminPasswordPrompt();
    }

    private void ShowAdminPasswordPrompt()
    {
        OnPropertyChanged(nameof(AdminModeEnabled));
        AdminPassword = string.Empty;
        AdminPasswordMessage = "请输入管理员密码，本次启动内有效。";
        AdminPasswordPromptVisible = true;
    }

    private void SubmitAdminPassword()
    {
        if (EnableAdminMode(AdminPassword))
        {
            CancelAdminPasswordPrompt();
            return;
        }

        AdminPassword = string.Empty;
        AdminPasswordMessage = "密码错误，请重新输入。";
    }

    private void CancelAdminPasswordPrompt()
    {
        AdminPassword = string.Empty;
        AdminPasswordPromptVisible = false;
    }

    private void RefreshWithConfirmation()
    {
        if (!ConfirmUnsavedFlowChanges("重新加载配置"))
        {
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        _draftFlowProject = null;
        _draftFlowConfig = null;
        _draftFlowTemplateSnapshotJson = null;
        _flowBaselineFingerprint = null;
        CloseNewFlowProjectDialog();

        var selectedName = SelectedProject?.ProjectName;
        Projects.Clear();
        foreach (var project in _projectRepository.LoadAll())
        {
            Projects.Add(project);
        }

        BootConfigFiles.Clear();
        foreach (var config in _bootConfigRepository.ListConfigFileNames())
        {
            BootConfigFiles.Add(config);
        }
        OnPropertyChanged(nameof(HasBootConfigTemplates));

        LoadAlgorithmCatalog();

        SelectedProject = Projects.FirstOrDefault(project => string.Equals(project.ProjectName, selectedName, StringComparison.OrdinalIgnoreCase))
            ?? Projects.FirstOrDefault();
        OnPropertyChanged(nameof(ConfigRootText));
        OnPropertyChanged(nameof(ScriptRootText));
        OnPropertyChanged(nameof(FlowConfigDirectory));
        OnPropertyChanged(nameof(FlowConfigFilePath));
        OnPropertyChanged(nameof(FormulaDatabaseDirectory));
        OnPropertyChanged(nameof(ProjectConfigPath));
        OnPropertyChanged(nameof(FlowConfigDirectoryDisplay));
        OnPropertyChanged(nameof(FormulaDatabaseDirectoryDisplay));
        OnPropertyChanged(nameof(ProjectConfigPathDisplay));
        AppendLog($"Configuration loaded: projects={Projects.Count}, boot={BootConfigFiles.Count}");
    }

    private void RefreshProjectList()
    {
        if (!ConfirmUnsavedFlowChanges("重新加载项目列表"))
        {
            return;
        }

        Refresh();
        CloseProjectEditor();
    }

    private void LoadAppSettings()
    {
        try
        {
            if (!File.Exists(AppSettingsFilePath))
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<AppSettingsSnapshot>(
                File.ReadAllText(AppSettingsFilePath),
                SettingsJsonOptions);
            if (settings is null)
            {
                return;
            }

            _keepRunningInTray = settings.KeepRunningInTray;
            _autoFlashEnabled = settings.AutoFlashEnabled;
            _autoSearchBaudRate = settings.AutoSearchBaudRate;
            _logFileEnabled = settings.LogFileEnabled;
            _logFilePath = string.IsNullOrWhiteSpace(settings.LogFilePath)
                ? _logFilePath
                : settings.LogFilePath;
            _logRetentionDays = NormalizeLogRetentionDays(settings.LogRetentionDays);
            _logMaxFileSizeMb = NormalizeLogMaxFileSizeMb(settings.LogMaxFileSizeMb);
            _logAutoScrollEnabled = settings.LogAutoScrollEnabled;
            _selectedDeviceType = string.IsNullOrWhiteSpace(settings.SelectedDeviceType)
                ? _selectedDeviceType
                : settings.SelectedDeviceType;
            _selectedBaudRate = string.IsNullOrWhiteSpace(settings.SelectedBaudRate)
                ? _selectedBaudRate
                : settings.SelectedBaudRate;
            _selectedCanChannel = string.IsNullOrWhiteSpace(settings.SelectedCanChannel)
                ? _selectedCanChannel
                : settings.SelectedCanChannel;
            _paths.ProjectConfigPathOverride = EmptyToNull(settings.ProjectConfigPath);
            _paths.BootConfigDirectoryOverride = EmptyToNull(settings.FlowConfigDirectory);
            _paths.FormulaDatabaseDirectoryOverride = EmptyToNull(settings.FormulaDatabaseDirectory);
        }
        catch
        {
            // Ignore malformed local settings and continue with defaults.
        }
    }

    private void SaveAppSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppSettingsFilePath)!);
            var settings = new AppSettingsSnapshot
            {
                KeepRunningInTray = KeepRunningInTray,
                AutoFlashEnabled = AutoFlashEnabled,
                AutoSearchBaudRate = AutoSearchBaudRate,
                LogFileEnabled = LogFileEnabled,
                LogFilePath = LogFilePath,
                LogRetentionDays = LogRetentionDays,
                LogMaxFileSizeMb = LogMaxFileSizeMb,
                LogAutoScrollEnabled = LogAutoScrollEnabled,
                SelectedDeviceType = SelectedDeviceType,
                SelectedBaudRate = SelectedBaudRate,
                SelectedCanChannel = SelectedCanChannel,
                ProjectConfigPath = _paths.ProjectConfigPathOverride,
                FlowConfigDirectory = _paths.BootConfigDirectoryOverride,
                FormulaDatabaseDirectory = _paths.FormulaDatabaseDirectoryOverride
            };
            File.WriteAllText(AppSettingsFilePath, JsonSerializer.Serialize(settings, SettingsJsonOptions));
        }
        catch
        {
            // Settings persistence should never block the flashing workflow.
        }
    }

    private void ImportBuiltinConfiguration()
    {
        if (!ConfirmUnsavedFlowChanges("导入内置配置"))
        {
            return;
        }

        try
        {
            CopyFileIfDifferent(_paths.DefaultProjectConfigPath, _paths.ProjectConfigPath);
            CopyDirectoryIfDifferent(_paths.DefaultBootConfigDirectory, _paths.BootConfigDirectory);
            CopyDirectoryIfDifferent(_paths.DefaultFormulaDatabaseDirectory, _paths.FormulaDatabaseDirectory);
            Refresh();
            SetStatus("内置配置已导入", DiagnosticStatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus("导入内置配置失败", DiagnosticStatusKind.Error);
            AppendLog($"Import builtin configuration failed: {ex.Message}");
        }
    }

    private void ClearRuntimeConfiguration()
    {
        var result = MessageBox.Show(
            "将清空当前项目配置和流程配置，是否继续？",
            "清空全部配置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        DiscardDraftFlowProject();

        try
        {
            if (File.Exists(_paths.ProjectConfigPath))
            {
                File.Delete(_paths.ProjectConfigPath);
            }

            if (Directory.Exists(_paths.BootConfigDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(_paths.BootConfigDirectory, "*.json"))
                {
                    File.Delete(file);
                }
            }

            Refresh();
            SetStatus("运行配置已清空", DiagnosticStatusKind.Warning);
        }
        catch (Exception ex)
        {
            SetStatus("清空运行配置失败", DiagnosticStatusKind.Error);
            AppendLog($"Clear runtime configuration failed: {ex.Message}");
        }
    }

    private void ChooseFlowConfigDirectory()
    {
        BrowseFolder("选择流程配置目录", FlowConfigDirectory, path =>
        {
            if (!ConfirmUnsavedFlowChanges("切换流程配置目录"))
            {
                return;
            }

            _paths.BootConfigDirectoryOverride = path;
            SaveAppSettings();
            Refresh();
        });
    }

    private void ChooseFormulaDatabaseDirectory()
    {
        BrowseFolder("选择配方数据库目录", FormulaDatabaseDirectory, path =>
        {
            if (!ConfirmUnsavedFlowChanges("切换配方数据库目录"))
            {
                return;
            }

            _paths.FormulaDatabaseDirectoryOverride = path;
            Directory.CreateDirectory(path);
            SaveAppSettings();
            Refresh();
        });
    }

    private void ChooseProjectConfigFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择项目配置文件",
            Filter = "项目配置 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(ProjectConfigPath))
                ? Path.GetDirectoryName(ProjectConfigPath)
                : _paths.DefaultConfigDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            if (!ConfirmUnsavedFlowChanges("切换项目配置文件"))
            {
                return;
            }

            _paths.ProjectConfigPathOverride = dialog.FileName;
            SaveAppSettings();
            Refresh();
        }
    }

    private void BrowseFolder(string title, string currentPath, Action<string> setPath)
    {
        var initialDirectory = Directory.Exists(currentPath)
            ? currentPath
            : Directory.Exists(Path.GetDirectoryName(currentPath))
                ? Path.GetDirectoryName(currentPath)
                : _paths.DefaultConfigDirectory;

        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = initialDirectory,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            setPath(dialog.FolderName);
        }
    }

    private void ChooseLogFile()
    {
        var currentPath = ResolveRuntimePath(LogFilePath);
        var currentDirectory = Path.GetDirectoryName(currentPath);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "选择日志文件",
            Filter = "日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
            FileName = Path.GetFileName(currentPath),
            DefaultExt = ".log",
            AddExtension = true,
            OverwritePrompt = false,
            InitialDirectory = Directory.Exists(currentDirectory)
                ? currentDirectory
                : _paths.RootDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            LogFilePath = ToRuntimeRelativePath(dialog.FileName);
        }
    }

    private void RestoreDefaultLogStorage()
    {
        LogFilePath = DefaultLogFilePath;
        LogRetentionDays = DefaultLogRetentionDays;
        LogMaxFileSizeMb = DefaultLogMaxFileSizeMb;
    }

    private void OpenLogFile()
    {
        try
        {
            var path = ResolveRuntimePath(LogFilePath);
            if (!File.Exists(path))
            {
                MessageBox.Show("日志文件不存在。", "打开日志文件", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus("打开日志文件失败", DiagnosticStatusKind.Error);
            AppendLog($"Open log file failed: {ex.Message}");
        }
    }

    private void ExportLog()
    {
        var logDirectory = Path.GetDirectoryName(ResolveRuntimePath(LogFilePath));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出日志",
            Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"DiagnosticFlashTool_{DateTime.Now:yyyyMMdd_HHmmss}.log",
            DefaultExt = ".log",
            AddExtension = true,
            InitialDirectory = Directory.Exists(logDirectory)
                ? logDirectory
                : _paths.RootDirectory
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllLines(dialog.FileName, FilteredLogEntries.Select(entry => entry.Text));
            SetStatus("日志已导出", DiagnosticStatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus("导出日志失败", DiagnosticStatusKind.Error);
            AppendLog($"Export log failed: {ex.Message}");
        }
    }

    private void ClearLogs()
    {
        LogEntries.Clear();
        FilteredLogEntries.Clear();
        RecentLogEntries.Clear();
        LatestLogEntry = null;
        RaiseLogSummaryProperties();
    }

    private void OpenPathLocation(string path)
    {
        try
        {
            var resolved = ResolveRuntimePath(path);
            var directory = Directory.Exists(resolved) ? resolved : Path.GetDirectoryName(resolved);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                MessageBox.Show("路径不存在。", "打开路径", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus("打开路径失败", DiagnosticStatusKind.Error);
            AppendLog($"Open path failed: {ex.Message}");
        }
    }

    private void LoadAlgorithmCatalog()
    {
        AlgorithmRows.Clear();
        AlgorithmRows.Add(new AlgorithmConfigRow("内置安全算法", "AES128_OneFunc", "DiagnosticFlashTool.Core", "可用"));

        AddAlgorithmScripts("安全算法脚本", Path.Combine(ScriptRootText, "Security"));
        AddAlgorithmScripts("CRC 算法脚本", Path.Combine(ScriptRootText, "CRC"));
        RefreshFlowAlgorithmOptions();
        OnPropertyChanged(nameof(AlgorithmCatalogText));
    }

    private void RefreshFlowAlgorithmOptions()
    {
        var algorithms = new[] { string.Empty, "AES128_OneFunc" }
            .Concat(AlgorithmRows
                .Where(row => row.Category.Contains("安全算法", StringComparison.Ordinal))
                .Select(row => row.Name))
            .Concat(FlowRows
                .Where(row => row.IsSecurityAlgorithmStep)
                .Select(row => row.SecurityAlgorithm))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Prepend(string.Empty)
            .ToList();

        FlowSecurityAlgorithmOptions.Clear();
        foreach (var algorithm in algorithms)
        {
            FlowSecurityAlgorithmOptions.Add(algorithm);
        }
    }

    private void AddAlgorithmScripts(string category, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.lua").OrderBy(Path.GetFileName))
        {
            AlgorithmRows.Add(new AlgorithmConfigRow(
                category,
                Path.GetFileNameWithoutExtension(file),
                file,
                "已发现"));
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            _canDevice = _canDeviceFactory.Create(SelectedDeviceType);
            _canDevice.FrameReceived += OnFrameReceived;
            _canDevice.FrameSent += OnFrameSent;

            await _canDevice.OpenAsync(new CanDeviceOptions
            {
                DeviceType = SelectedDeviceType,
                BaudRate = HexUtil.ParseBaudRate(SelectedBaudRate),
                Channel = uint.TryParse(SelectedCanChannel, out var channel) ? channel : 0
            }, cancellationToken);

            IsConnected = true;
            SetStatus("CAN connected", DiagnosticStatusKind.Success);
            AppendLog($"CAN connected: {SelectedDeviceType}, {SelectedBaudRate}, channel={SelectedCanChannel}");
        }
        catch (Exception ex)
        {
            SetStatus("Connect failed", DiagnosticStatusKind.Error);
            AppendLog($"Connect failed: {ex.Message}");
            IsConnected = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ToggleConnectionAsync(CancellationToken cancellationToken)
        => IsConnected ? DisconnectAsync(cancellationToken) : ConnectAsync(cancellationToken);

    private async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            if (_canDevice is not null)
            {
                _canDevice.FrameReceived -= OnFrameReceived;
                _canDevice.FrameSent -= OnFrameSent;
                await _canDevice.CloseAsync(cancellationToken);
                await _canDevice.DisposeAsync();
                _canDevice = null;
            }

            IsConnected = false;
            SetStatus("CAN disconnected", DiagnosticStatusKind.Neutral);
            AppendLog("CAN disconnected.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Releases the CAN device when the hosting window shuts down. Without this the
    /// native driver handle (for example ZLG ControlCAN) stays open until the OS
    /// reclaims the process, which can block the next launch from opening the device.
    /// </summary>
    /// <remarks>
    /// Intentionally avoids touching view-model state so it can be awaited while the
    /// window is closing.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var device = _canDevice;
        _canDevice = null;
        if (device is null)
        {
            return;
        }

        device.FrameReceived -= OnFrameReceived;
        device.FrameSent -= OnFrameSent;
        try
        {
            await device.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteLogLine($"--- 释放 CAN 设备失败 ---{Environment.NewLine}{ex}");
        }
        finally
        {
            await device.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task StartFlashAsync(CancellationToken cancellationToken)
    {
        if (_canDevice is null || SelectedProject is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Progress = 0;
            DownloadStatusKind = DiagnosticStatusKind.Running;
            SetStatus("Flashing", DiagnosticStatusKind.Running);

            var bootFile = SelectedBootConfig ?? SelectedProject.BootConfigFile;
            var bootConfig = _bootConfigRepository.Load(bootFile);
            var preflightIssues = ValidateAppFlow(bootConfig);
            var preflightErrors = preflightIssues
                .Where(issue => issue.Kind == FlashFlowIssueKind.Error)
                .ToList();
            if (preflightErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "BOOT 流程校验未通过：" + Environment.NewLine
                    + string.Join(Environment.NewLine, preflightErrors.Select(issue => "  - " + issue)));
            }

            foreach (var warning in preflightIssues.Where(issue => issue.Kind == FlashFlowIssueKind.Warning))
            {
                AppendLog($"流程提示：{warning}");
            }

            var firmwareSet = LoadFirmwareSet(SelectedProject, bootConfig);

            var options = new DiagnosticTransportOptions
            {
                PhysicalRequestId = HexUtil.ParseUInt32(SelectedProject.PhysicalRequestId),
                FunctionalRequestId = HexUtil.ParseUInt32(SelectedProject.FunctionalRequestId),
                ResponseId = HexUtil.ParseUInt32(SelectedProject.ResponseAddressId),
                Channel = uint.TryParse(SelectedCanChannel, out var selectedChannel)
                    ? selectedChannel
                    : 0
            };

            using var transport = new IsoTpTransport(_canDevice, options);
            var udsClient = new UdsClient(transport);
            IFlashFlowExecutor executor = new FlashFlowExecutor(udsClient, _seedKeyAlgorithmRegistry);
            var progress = new Progress<FlashProgress>(item =>
            {
                Progress = Math.Clamp(item.Percent, 0, 100);
                DownloadStatusKind = DiagnosticStatusKind.Running;
                SetStatus(item.Message, DiagnosticStatusKind.Running);
            });

            var result = await executor.ExecuteAsync(
                new FlashExecutionRequest(
                    bootConfig,
                    firmwareSet),
                progress,
                cancellationToken);

            foreach (var message in result.LogMessages)
            {
                AppendLog(message);
            }

            DownloadStatusKind = result.Success ? DiagnosticStatusKind.Success : DiagnosticStatusKind.Error;
            var resultMessage = result.Success
                ? "刷写完成"
                : string.IsNullOrWhiteSpace(result.UserMessage) ? "刷写失败" : result.UserMessage;
            SetStatus(resultMessage, DownloadStatusKind);
            Progress = result.Success ? 100 : Progress;
        }
        catch (OperationCanceledException)
        {
            DownloadStatusKind = DiagnosticStatusKind.Warning;
            SetStatus("刷写已取消", DiagnosticStatusKind.Warning);
            AppendLog("刷写已取消。");
        }
        catch (Exception ex)
        {
            DownloadStatusKind = DiagnosticStatusKind.Error;
            SetStatus(ex.Message, DiagnosticStatusKind.Error);
            AppendLog($"刷写失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendManualFrameAsync(CancellationToken cancellationToken)
    {
        if (_canDevice is null)
        {
            return;
        }

        try
        {
            var data = HexUtil.ParseBytes(ManualFrameData);
            if (data.Length > 8)
            {
                throw new InvalidOperationException("Classic CAN frame data is limited to 8 bytes.");
            }

            var channel = uint.TryParse(SelectedCanChannel, out var parsedChannel) ? parsedChannel : 0;
            var frameId = ParseManualFrameId(ManualFrameId);
            if (frameId > 0x1FFFFFFF)
            {
                throw new InvalidOperationException("ID超出有效范围");
            }

            var isExtended = frameId > 0x7FF;
            var frame = new CanFrame((uint)frameId, data, channel, isExtended, false);
            await _canDevice.SendAsync(frame, cancellationToken);
            AppendLog($"Manual TX: {frame}");
        }
        catch (Exception ex)
        {
            AppendLog($"Manual TX failed: {ex.Message}");
        }
    }

    private FirmwareSet LoadFirmwareSet(ProjectConfigEntry project, BootConfig bootConfig)
    {
        // The flow is the specification: a download step means the file has to exist.
        // Silently skipping a missing file is what allowed a flow to report success
        // without having downloaded anything.
        var driverPath = FirstConfiguredPath(DriverFilePath, project.DriveFilePath);
        var applicationPath = FirstConfiguredPath(ApplicationFilePath, project.FlashFilePath);

        var driver = LoadFirmwareImage(
            driverPath,
            FirmwareImageKind.Driver,
            "驱动",
            RequiresDownloadStep(bootConfig, FlashStepKind.DownloadDriver),
            0);

        var application = LoadFirmwareImage(
            applicationPath,
            FirmwareImageKind.Application,
            "应用",
            RequiresDownloadStep(bootConfig, FlashStepKind.DownloadApplication),
            HexUtil.ParseUInt32(project.AppStartAddress, 0));

        return new FirmwareSet { Driver = driver, Application = application };
    }

    private static string FirstConfiguredPath(string preferred, string? fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback ?? string.Empty : preferred;
    }

    private static bool RequiresDownloadStep(BootConfig bootConfig, FlashStepKind expectedKind)
    {
        return bootConfig.Flow.Any(step =>
            FlashStepTypes.TryParse(step.StepType, out var kind) && kind == expectedKind);
    }

    private FirmwareImage? LoadFirmwareImage(
        string path,
        FirmwareImageKind kind,
        string label,
        bool requiresDownload,
        uint fallbackAddress)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (requiresDownload)
            {
                throw new InvalidOperationException(
                    $"BOOT 流程包含 {label} 下载步骤，但没有选择 {label} 固件文件。");
            }

            return null;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{label} 固件文件不存在：{path}", path);
        }

        var image = _firmwareLoader.Load(path, kind, fallbackAddress);
        AppendLog($"{label} image loaded: {Path.GetFileName(path)}, {image.Length} bytes");
        return image;
    }

    private void OpenNewProjectEditor()
    {
        var nextNo = Projects.Count == 0 ? 1 : Projects.Max(project => project.ProjectNo) + 1;
        ProjectEditor.BeginNew(nextNo, BootConfigFiles.FirstOrDefault() ?? string.Empty);
        SetProjectEditorMode(null, "新建项目");
        IsProjectEditorVisible = true;
    }

    private void EditSelectedProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        ProjectEditor.Load(SelectedProject);
        SetProjectEditorMode(SelectedProject, "编辑项目");
        IsProjectEditorVisible = true;
    }

    private void SaveProjectEditor()
    {
        var project = ProjectEditor.CreateProject(out var editorValidation);
        if (project is null)
        {
            SetStatus("项目保存失败", DiagnosticStatusKind.Error);
            AppendLog(editorValidation);
            return;
        }

        var projectsToSave = Projects.ToList();
        var editedIndex = _editingProject is null ? -1 : projectsToSave.IndexOf(_editingProject);
        if (_editingProject is not null && editedIndex < 0)
        {
            SetStatus("项目保存失败", DiagnosticStatusKind.Error);
            AppendLog("项目已被刷新，请重新打开后再保存。");
            return;
        }

        if (editedIndex >= 0)
        {
            projectsToSave[editedIndex] = project;
        }
        else
        {
            projectsToSave.Add(project);
        }

        var validation = ValidateProjects(projectsToSave);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            SetStatus("项目保存失败", DiagnosticStatusKind.Error);
            AppendLog(validation);
            return;
        }

        try
        {
            _projectRepository.SaveAll(projectsToSave);
        }
        catch (Exception ex)
        {
            SetStatus("项目保存失败", DiagnosticStatusKind.Error);
            AppendLog($"项目保存失败: {ex.Message}");
            return;
        }

        if (editedIndex >= 0)
        {
            Projects[editedIndex] = project;
        }
        else
        {
            Projects.Add(project);
        }

        SelectedProject = project;
        SetStatus("项目已保存", DiagnosticStatusKind.Success);
        AppendLog($"项目已保存: {project.ProjectName}");
        CloseProjectEditor();
    }

    private void DeleteProjectEditor()
    {
        if (_editingProject is null)
        {
            return;
        }

        if (ReferenceEquals(_editingProject, _draftFlowProject))
        {
            SetStatus("请先在流程配置页保存空白项目", DiagnosticStatusKind.Warning);
            return;
        }

        var index = Projects.IndexOf(_editingProject);
        if (index < 0)
        {
            CloseProjectEditor();
            return;
        }

        var projectName = _editingProject.ProjectName;
        if (MessageBox.Show(
                $"确定删除项目“{projectName}”？",
                "删除项目",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var projectsToSave = Projects.ToList();
        projectsToSave.RemoveAt(index);
        try
        {
            _projectRepository.SaveAll(projectsToSave);
        }
        catch (Exception ex)
        {
            SetStatus("项目删除失败", DiagnosticStatusKind.Error);
            AppendLog($"项目删除失败: {ex.Message}");
            return;
        }

        Projects.RemoveAt(index);
        SelectedProject = Projects.ElementAtOrDefault(Math.Clamp(index, 0, Math.Max(0, Projects.Count - 1)));
        SetStatus("项目已删除", DiagnosticStatusKind.Success);
        AppendLog($"项目已删除: {projectName}");
        CloseProjectEditor();
    }

    private void CloseProjectEditor()
    {
        SetProjectEditorMode(null, "新建项目");
        IsProjectEditorVisible = false;
    }

    private void SetProjectEditorMode(ProjectConfigEntry? editingProject, string title)
    {
        _editingProject = editingProject;
        ProjectEditorTitle = title;
        OnPropertyChanged(nameof(IsEditingProject));
        RaiseProjectEditorCommandStates();
    }

    private void AddProject()
    {
        var nextNo = Projects.Count == 0 ? 1 : Projects.Max(project => project.ProjectNo) + 1;
        var project = new ProjectConfigEntry
        {
            ProjectName = $"New Project {nextNo}",
            ProjectNo = nextNo,
            BaudRate = "500K",
            PhysicalRequestId = "0x18DA5535",
            FunctionalRequestId = "0x18DA55FF",
            ResponseAddressId = "0x18DA3555",
            BootConfigFile = BootConfigFiles.FirstOrDefault() ?? string.Empty
        };

        Projects.Add(project);
        SelectedProject = project;
        AppendLog($"Project added: {project.ProjectName}");
    }

    private void DeleteSelectedProject()
    {
        if (SelectedProject is null || ReferenceEquals(SelectedProject, _draftFlowProject))
        {
            return;
        }

        var index = Projects.IndexOf(SelectedProject);
        AppendLog($"Project deleted: {SelectedProject.ProjectName}");
        Projects.Remove(SelectedProject);
        SelectedProject = Projects.ElementAtOrDefault(Math.Clamp(index, 0, Math.Max(0, Projects.Count - 1)));
    }

    private void SaveProjects()
    {
        if (_draftFlowProject is not null)
        {
            SetStatus("请先在流程配置页保存空白项目", DiagnosticStatusKind.Warning);
            return;
        }

        var validation = ValidateProjects(Projects);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            SetStatus("Project validation failed", DiagnosticStatusKind.Error);
            AppendLog(validation);
            return;
        }

        _projectRepository.SaveAll(Projects);
        SetStatus("Projects saved", DiagnosticStatusKind.Success);
        AppendLog($"Projects saved: {Projects.Count}");
    }

    private static string ValidateProjects(IEnumerable<ProjectConfigEntry> projects)
    {
        var projectList = projects.ToList();
        var issues = new List<string>();
        foreach (var project in projectList)
        {
            if (string.IsNullOrWhiteSpace(project.ProjectName))
            {
                issues.Add("Project name cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(project.BootConfigFile))
            {
                issues.Add($"Project {project.ProjectName}: boot config is empty.");
            }
        }

        issues.AddRange(projectList.GroupBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => $"Duplicate project name: {group.Key}"));
        issues.AddRange(projectList.GroupBy(project => project.ProjectNo)
            .Where(group => group.Key > 0 && group.Count() > 1)
            .Select(group => $"Duplicate project number: {group.Key}"));

        return string.Join(Environment.NewLine, issues.Distinct());
    }

    private void ReloadFlowFromSelected()
    {
        if (!ConfirmUnsavedFlowChanges("重新加载当前配置"))
        {
            return;
        }

        LoadFlowFromSelected();
    }

    private void LoadFlowFromSelected()
    {
        CloseAddFlowStepDialog();
        FlowRows.Clear();
        _loadedFlowConfig = null;
        _flowBaselineFingerprint = null;
        SelectedFlowStep = null;

        if (SelectedProject is null)
        {
            FlowValidationText = "未选择项目。";
            RefreshFlowAlgorithmOptions();
            MarkFlowAsBaseline();
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedBootConfig))
        {
            FlowValidationText = $"项目“{SelectedProject.ProjectName}”未配置 BOOT 配置文件。";
            RefreshFlowAlgorithmOptions();
            MarkFlowAsBaseline();
            return;
        }

        try
        {
            _loadedFlowConfig = ReferenceEquals(SelectedProject, _draftFlowProject)
                ? _draftFlowConfig ?? throw new InvalidOperationException("配置草稿不存在。")
                : _bootConfigRepository.Load(SelectedBootConfig);

            PopulateFlowRows(_loadedFlowConfig);
            var source = ReferenceEquals(SelectedProject, _draftFlowProject) ? "draft" : SelectedBootConfig;
            AppendLog($"Flow loaded: {source}, steps={FlowRows.Count}");
        }
        catch (Exception ex)
        {
            FlowValidationText = ex.Message;
            RefreshFlowAlgorithmOptions();
            MarkFlowAsBaseline();
            AppendLog($"Flow load failed: {ex.Message}");
        }
    }

    private void SaveFlowConfig() => TrySaveFlowConfig();

    private bool TrySaveFlowConfig()
    {
        if (string.IsNullOrWhiteSpace(SelectedBootConfig))
        {
            SetStatus("当前配置没有可保存的 BOOT 文件", DiagnosticStatusKind.Warning);
            return false;
        }

        if (!ValidateFlowConfig())
        {
            SetStatus("Flow validation failed", DiagnosticStatusKind.Error);
            return false;
        }

        try
        {
            var isDraft = ReferenceEquals(SelectedProject, _draftFlowProject);
            var config = _loadedFlowConfig
                ?? (isDraft
                    ? _draftFlowConfig ?? throw new InvalidOperationException("配置草稿不存在。")
                    : _bootConfigRepository.Load(SelectedBootConfig));

            // 只更新 flow；scripts 及模型已承载的未编辑字段由已加载配置原样写回。
            config.Flow = FlowRows.OrderBy(row => row.Id).Select(row => row.ToConfig()).ToList();
            if (string.IsNullOrWhiteSpace(config.Name))
            {
                config.Name = Path.GetFileNameWithoutExtension(SelectedBootConfig);
            }

            if (isDraft)
            {
                PersistDraftFlowProject(config);
            }
            else
            {
                _bootConfigRepository.Save(SelectedBootConfig, config);
            }

            _loadedFlowConfig = config;
            MarkFlowAsBaseline();
            SetStatus(isDraft ? "配置已保存" : "流程已保存", DiagnosticStatusKind.Success);
            AppendLog($"Flow saved: {SelectedBootConfig}, steps={FlowRows.Count}");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("流程保存失败", DiagnosticStatusKind.Error);
            AppendLog($"Flow save failed: {ex.Message}");
            return false;
        }
    }

    private void PopulateFlowRows(BootConfig config)
    {
        foreach (var step in config.Flow.OrderBy(step => step.Id))
        {
            FlowRows.Add(FlowStepEditorRow.FromConfig(step));
        }

        SelectedFlowStep = FlowRows.FirstOrDefault();
        RefreshFlowAlgorithmOptions();
        ValidateFlowConfig();
        MarkFlowAsBaseline();
    }

    private void MarkFlowAsBaseline()
    {
        _flowBaselineFingerprint = CreateFlowFingerprint();
    }

    private string CreateFlowFingerprint() => JsonSerializer.Serialize(
        FlowRows.OrderBy(row => row.Id).Select(row => row.ToConfig()).ToList());

    private bool HasUnsavedFlowChanges()
    {
        if (SelectedProject is null)
        {
            return false;
        }

        if (ReferenceEquals(SelectedProject, _draftFlowProject))
        {
            return true;
        }

        return _flowBaselineFingerprint is not null
            && !string.Equals(_flowBaselineFingerprint, CreateFlowFingerprint(), StringComparison.Ordinal);
    }

    private void OpenNewFlowProjectDialog()
    {
        if (!ConfirmUnsavedFlowChanges("新建配置"))
        {
            return;
        }

        CloseAddFlowStepDialog();
        NewFlowProjectName = string.Empty;
        SelectedFlowProjectTemplate = BootConfigFiles.FirstOrDefault();
        CreateFlowProjectFromTemplate = SelectedFlowProjectTemplate is not null;
        NewFlowProjectValidationText = string.Empty;
        OnPropertyChanged(nameof(HasBootConfigTemplates));
        IsNewFlowProjectDialogVisible = true;
    }

    private void DiscardDraftFlowProject(bool selectFallback = true)
    {
        if (_draftFlowProject is null)
        {
            return;
        }

        var draft = _draftFlowProject;
        var index = Projects.IndexOf(draft);
        var wasSelected = ReferenceEquals(SelectedProject, draft);
        _suppressFlowSelectionPrompt = true;
        try
        {
            _draftFlowProject = null;
            _draftFlowConfig = null;
            _draftFlowTemplateSnapshotJson = null;
            _loadedFlowConfig = null;
            _flowBaselineFingerprint = null;
            FlowRows.Clear();
            SelectedFlowStep = null;
            if (index >= 0)
            {
                Projects.RemoveAt(index);
            }

            if (wasSelected && selectFallback)
            {
                SelectedProject = Projects.ElementAtOrDefault(
                    Math.Clamp(index, 0, Math.Max(0, Projects.Count - 1)));
            }
        }
        finally
        {
            _suppressFlowSelectionPrompt = false;
        }

        RaiseCommandStates();
        AppendLog($"Flow configuration draft discarded: {draft.ProjectName}");
    }

    private bool ConfirmUnsavedFlowChanges(
        string action,
        bool selectFallbackWhenDiscardingDraft = true)
    {
        if (!HasUnsavedFlowChanges())
        {
            return true;
        }

        var configName = SelectedProject?.ProjectName ?? SelectedBootConfig ?? "当前配置";
        var result = MessageBox.Show(
            $"配置“{configName}”存在未保存的修改。{action}前是否保存？\n\n"
            + "选择“是”保存修改，选择“否”放弃修改，选择“取消”继续编辑。",
            "未保存的修改",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.Yes)
        {
            return TrySaveFlowConfig();
        }

        if (ReferenceEquals(SelectedProject, _draftFlowProject))
        {
            DiscardDraftFlowProject(selectFallbackWhenDiscardingDraft);
        }
        else
        {
            LoadFlowFromSelected();
        }

        return true;
    }

    private void CloseNewFlowProjectDialog()
    {
        IsNewFlowProjectDialogVisible = false;
        NewFlowProjectName = string.Empty;
        SelectedFlowProjectTemplate = null;
        NewFlowProjectValidationText = string.Empty;
    }

    private void ConfirmNewFlowProject()
    {
        var projectName = NewFlowProjectName.Trim();
        var configFileName = $"{projectName}.json";
        var validation = ValidateNewFlowProject(projectName, configFileName);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            NewFlowProjectValidationText = validation;
            return;
        }

        try
        {
            var project = CreateFlowProjectEntry(projectName, configFileName);
            var templateSource = CreateFlowProjectFromTemplate
                ? SelectedFlowProjectTemplate!
                : null;
            var templateSnapshotJson = templateSource is null
                ? null
                : _bootConfigRepository.CreateSnapshotJson(templateSource, projectName);
            var config = templateSource is null
                ? new BootConfig()
                : _bootConfigRepository.Load(templateSource);
            config.Name = projectName;

            _draftFlowProject = project;
            _draftFlowConfig = config;
            _draftFlowTemplateSnapshotJson = templateSnapshotJson;
            Projects.Add(project);
            SelectedProject = project;
            SetStatus("已创建配置草稿，保存后写入文件", DiagnosticStatusKind.Neutral);
            AppendLog(templateSource is null
                ? $"Blank flow configuration draft created: {projectName}"
                : $"Flow configuration draft created: {projectName}, source={templateSource}");

            CloseNewFlowProjectDialog();
            RaiseCommandStates();
        }
        catch (Exception ex)
        {
            NewFlowProjectValidationText = ex.Message;
            SetStatus("新建配置失败", DiagnosticStatusKind.Error);
            AppendLog($"Flow configuration creation failed: {ex.Message}");
        }
    }

    private string ValidateNewFlowProject(string projectName, string configFileName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return "配置名称不能为空。";
        }

        if (projectName is "." or ".."
            || projectName.EndsWith('.')
            || projectName.EndsWith(' ')
            || projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "配置名称包含不能用于文件名的字符。";
        }

        if (Projects.Any(project => string.Equals(
                project.ProjectName,
                projectName,
                StringComparison.OrdinalIgnoreCase)))
        {
            return $"配置“{projectName}”已存在。";
        }

        var targetPath = Path.Combine(FlowConfigDirectory, configFileName);
        if (File.Exists(targetPath)
            || Projects.Any(project => BootConfigReferencesSameFile(project.BootConfigFile, configFileName)))
        {
            return $"配置文件“{configFileName}”已被使用。";
        }

        if (CreateFlowProjectFromTemplate && string.IsNullOrWhiteSpace(SelectedFlowProjectTemplate))
        {
            return "请选择一个已有 BOOT 配置。";
        }

        return string.Empty;
    }

    private ProjectConfigEntry CreateFlowProjectEntry(string projectName, string configFileName)
    {
        var nextNo = Projects.Count == 0 ? 1 : Projects.Max(project => project.ProjectNo) + 1;
        return new ProjectConfigEntry
        {
            ProjectName = projectName,
            ProjectNo = nextNo,
            BaudRate = "500K",
            PhysicalRequestId = "0x18DA5535",
            FunctionalRequestId = "0x18DA55FF",
            ResponseAddressId = "0x18DA3555",
            BootConfigFile = configFileName
        };
    }

    private void PersistDraftFlowProject(BootConfig config)
    {
        if (_draftFlowProject is null || !ReferenceEquals(SelectedProject, _draftFlowProject))
        {
            throw new InvalidOperationException("当前配置不是未保存草稿。");
        }

        var draftProject = _draftFlowProject;
        var templateSnapshotJson = _draftFlowTemplateSnapshotJson;
        var validation = ValidateProjects(Projects);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            throw new InvalidOperationException(validation);
        }

        SaveNewBootConfigAndProjects(
            draftProject.BootConfigFile,
            () =>
            {
                if (templateSnapshotJson is not null)
                {
                    _bootConfigRepository.SaveSnapshotJson(
                        draftProject.BootConfigFile,
                        templateSnapshotJson);
                }

                _bootConfigRepository.Save(draftProject.BootConfigFile, config);
            },
            Projects);
        AddBootConfigFile(draftProject.BootConfigFile);
        _draftFlowProject = null;
        _draftFlowConfig = null;
        _draftFlowTemplateSnapshotJson = null;
        RaiseCommandStates();
    }

    private void SaveNewBootConfigAndProjects(
        string configFileName,
        Action saveBootConfig,
        IEnumerable<ProjectConfigEntry> projects)
    {
        var targetPath = Path.Combine(FlowConfigDirectory, configFileName);
        if (File.Exists(targetPath))
        {
            throw new IOException($"配置文件已存在：{targetPath}");
        }

        try
        {
            saveBootConfig();
            _projectRepository.SaveAll(projects);
        }
        catch
        {
            try
            {
                File.Delete(targetPath);
            }
            catch (Exception rollbackException)
            {
                AppendLog($"Project creation rollback failed: {rollbackException.Message}");
            }

            throw;
        }
    }

    private void AddBootConfigFile(string configFileName)
    {
        if (BootConfigFiles.Contains(configFileName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var insertIndex = 0;
        while (insertIndex < BootConfigFiles.Count
               && StringComparer.OrdinalIgnoreCase.Compare(BootConfigFiles[insertIndex], configFileName) < 0)
        {
            insertIndex++;
        }

        BootConfigFiles.Insert(insertIndex, configFileName);
        OnPropertyChanged(nameof(HasBootConfigTemplates));
    }

    private bool BootConfigReferencesSameFile(string configReference, string configFileName)
    {
        if (string.IsNullOrWhiteSpace(configReference))
        {
            return false;
        }

        var existingPath = Path.IsPathRooted(configReference)
            ? configReference
            : Path.Combine(FlowConfigDirectory, configReference);
        var targetPath = Path.Combine(FlowConfigDirectory, configFileName);
        return string.Equals(
            Path.GetFullPath(existingPath),
            Path.GetFullPath(targetPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private bool ValidateFlowConfig()
    {
        var issues = new List<string>();
        var warnings = new List<string>();
        if (FlowRows.Count == 0)
        {
            issues.Add("流程中未配置节点。");
        }

        issues.AddRange(FlowRows.GroupBy(row => row.Id)
            .Where(group => group.Count() > 1)
            .Select(group => $"流程节点 ID 重复：{group.Key}"));

        foreach (var row in FlowRows)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                issues.Add($"节点 {row.Id}：名称不能为空。");
            }

            var isDownload = FlashStepTypes.IsDownload(row.StepType);
            if (!isDownload && string.IsNullOrWhiteSpace(row.Service))
            {
                issues.Add($"节点 {row.Id}：服务不能为空。");
            }

            if (!string.IsNullOrWhiteSpace(row.Service) && !HexUtil.TryParseByte(row.Service, out _))
            {
                issues.Add($"节点 {row.Id}：服务必须是单字节十六进制值。");
            }

            if (!string.IsNullOrWhiteSpace(row.SubService) && !HexUtil.TryParseByte(row.SubService, out _))
            {
                issues.Add($"节点 {row.Id}：子服务必须是单字节十六进制值。");
            }
        }

        // Flow-level semantics, shared with the flash executor, so the editor cannot
        // accept a flow that the executor would refuse - or worse, would only partly
        // execute while still reporting success.
        if (FlowRows.Count > 0)
        {
            var draft = new BootConfig
            {
                Name = SelectedBootConfig ?? string.Empty,
                Flow = FlowRows.OrderBy(row => row.Id).Select(row => row.ToConfig()).ToList()
            };

            foreach (var issue in ValidateAppFlow(draft))
            {
                if (issue.Kind == FlashFlowIssueKind.Error)
                {
                    issues.Add(issue.ToString());
                }
                else
                {
                    warnings.Add(issue.ToString());
                }
            }
        }

        FlowValidationText = issues.Count == 0 && warnings.Count == 0
            ? $"校验通过：共 {FlowRows.Count} 个流程节点。"
            : string.Join(
                Environment.NewLine,
                issues.Select(text => $"错误：{text}").Concat(warnings.Select(text => $"提示：{text}")));

        // Warnings must not block saving: a declaration the executor does not honour yet
        // is reported, but it does not stop the operator from editing the flow.
        return issues.Count == 0;
    }

    private IReadOnlyList<FlashFlowIssue> ValidateAppFlow(BootConfig config)
    {
        var policyIssues = BuiltInFlowTemplateValidator.Validate(config).ToList();
        foreach (var issue in FlashFlowValidator.Validate(config, _seedKeyAlgorithmRegistry))
        {
            if (!IsCoveredByTemplatePolicy(issue, policyIssues))
            {
                policyIssues.Add(issue);
            }
        }

        return policyIssues;
    }

    private static bool IsCoveredByTemplatePolicy(
        FlashFlowIssue executionIssue,
        IReadOnlyList<FlashFlowIssue> policyIssues)
    {
        ReadOnlySpan<string> constraintNames = ["crcAlgorithm", "securityAlgorithm", "algorithmParams"];
        foreach (var constraintName in constraintNames)
        {
            if (executionIssue.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase)
                && policyIssues.Any(policyIssue =>
                    policyIssue.StepId == executionIssue.StepId
                    && policyIssue.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private void OpenAddFlowStepDialog()
    {
        SetFlowStepTemplateDialogMode(false);
        SelectedFlowStepTemplate = null;
        IsAddFlowStepDialogVisible = true;
    }

    private void OpenReplaceFlowStepDialog()
    {
        if (SelectedFlowStep is null)
        {
            return;
        }

        SetFlowStepTemplateDialogMode(true);
        SelectedFlowStepTemplate = null;
        IsAddFlowStepDialogVisible = true;
    }

    private void ConfirmSelectedFlowStepTemplate()
    {
        if (SelectedFlowStepTemplate is null)
        {
            return;
        }

        if (IsReplacingFlowStepTemplate)
        {
            if (SelectedFlowStep is null)
            {
                return;
            }

            SelectedFlowStep.ApplyTemplate(SelectedFlowStepTemplate.Definition, preserveCompatibleValues: true);
        }
        else
        {
            var nextId = FlowRows.Count == 0 ? 1 : FlowRows.Max(row => row.Id) + 1;
            var row = SelectedFlowStepTemplate.CreateRow(nextId);

            FlowRows.Add(row);
            SelectedFlowStep = row;
        }

        CloseAddFlowStepDialog();
        RefreshFlowAlgorithmOptions();
        ValidateFlowConfig();
    }

    private void CloseAddFlowStepDialog()
    {
        IsAddFlowStepDialogVisible = false;
        SelectedFlowStepTemplate = null;
        SetFlowStepTemplateDialogMode(false);
    }

    private void SetFlowStepTemplateDialogMode(bool isReplacing)
    {
        if (_isReplacingFlowStepTemplate == isReplacing)
        {
            return;
        }

        _isReplacingFlowStepTemplate = isReplacing;
        OnPropertyChanged(nameof(IsReplacingFlowStepTemplate));
        OnPropertyChanged(nameof(FlowStepTemplateDialogTitle));
        OnPropertyChanged(nameof(FlowStepTemplateDialogDescription));
        OnPropertyChanged(nameof(FlowStepTemplateDialogConfirmText));
        ConfirmAddFlowStepCommand.RaiseCanExecuteChanged();
    }

    private void DeleteSelectedFlowStep()
    {
        if (SelectedFlowStep is null)
        {
            return;
        }

        var index = FlowRows.IndexOf(SelectedFlowStep);
        FlowRows.Remove(SelectedFlowStep);
        SelectedFlowStep = FlowRows.ElementAtOrDefault(Math.Clamp(index, 0, Math.Max(0, FlowRows.Count - 1)));
        RefreshFlowAlgorithmOptions();
        ValidateFlowConfig();
    }

    private void MoveSelectedFlowStep(int delta)
    {
        if (SelectedFlowStep is null)
        {
            return;
        }

        var index = FlowRows.IndexOf(SelectedFlowStep);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= FlowRows.Count)
        {
            return;
        }

        MoveFlowStepToIndex(SelectedFlowStep, target);
    }

    public void MoveFlowStepToIndex(FlowStepEditorRow step, int targetIndex)
    {
        var sourceIndex = FlowRows.IndexOf(step);
        if (sourceIndex < 0 || FlowRows.Count == 0)
        {
            return;
        }

        var boundedTargetIndex = Math.Clamp(targetIndex, 0, FlowRows.Count - 1);
        if (sourceIndex == boundedTargetIndex)
        {
            return;
        }

        FlowRows.Move(sourceIndex, boundedTargetIndex);
        for (var i = 0; i < FlowRows.Count; i++)
        {
            FlowRows[i].Id = i + 1;
        }

        SelectedFlowStep = step;
        ValidateFlowConfig();
    }

    private void ApplySelectedProject()
    {
        if (SelectedProject is null)
        {
            SelectedBootConfig = null;
            return;
        }

        SelectedBootConfig = string.IsNullOrWhiteSpace(SelectedProject.BootConfigFile)
            ? null
            : SelectedProject.BootConfigFile.Trim();
        SelectedBaudRate = SelectedProject.BaudRate;
        DriverFilePath = SelectedProject.DriveFilePath;
        ApplicationFilePath = SelectedProject.FlashFilePath;
    }

    private void BrowseFirmware(Action<string> setPath)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Firmware files (*.s19;*.srec;*.mot;*.hex;*.bin)|*.s19;*.srec;*.mot;*.hex;*.bin|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            setPath(dialog.FileName);
        }
    }

    private void ToggleFrameCapture()
    {
        IsFrameCapturePaused = !IsFrameCapturePaused;
        AppendLog(IsFrameCapturePaused ? "CAN 接收显示已暂停" : "CAN 接收显示已继续");
    }

    private void ToggleFrameFilter()
    {
        IsFrameFilterEnabled = !IsFrameFilterEnabled;
        Frames.Clear();
        AppendLog(IsFrameFilterEnabled
            ? "CAN ID 过滤已开启，仅显示当前项目相关 ID"
            : "CAN ID 过滤已关闭");
    }

    private void ClearFrames()
    {
        Frames.Clear();
        _frameHistory.Clear();
    }

    private void ExportFrames()
    {
        if (_frameHistory.Count == 0)
        {
            SetStatus("当前没有可导出的报文", DiagnosticStatusKind.Warning);
            AppendLog("CAN 报文导出失败：当前没有可导出的报文");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"can-frames-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var exportFrames = _frameHistory.ToArray();
            var lines = new[] { "Time,Direction,Channel,ID,Format,DLC,Data" }
                .Concat(exportFrames.Select(frame => string.Join(
                    ",",
                    frame.Timestamp,
                    frame.Direction,
                    frame.Channel,
                    frame.IdText,
                    frame.FormatText,
                    frame.Dlc,
                    frame.DataText)));
            File.WriteAllLines(dialog.FileName, lines);
            SetStatus("报文已导出", DiagnosticStatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus("导出报文失败", DiagnosticStatusKind.Error);
            AppendLog($"Export frames failed: {ex.Message}");
        }
    }

    private void OnFrameReceived(object? sender, CanFrame frame) => AddFrame("RX", frame);

    private void OnFrameSent(object? sender, CanFrame frame) => AddFrame("TX", frame);

    private static ulong ParseManualFrameId(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (text.Length == 0)
        {
            throw new FormatException("CAN ID 不能为空。");
        }

        try
        {
            return ulong.Parse(
                text,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("ID超出有效范围");
        }
    }

    private static bool TryParseConfiguredCanId(string? value, out uint id)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            id = 0;
            return false;
        }

        try
        {
            id = HexUtil.ParseUInt32(value);
            return id > 0;
        }
        catch
        {
            try
            {
                var parsedId = ParseManualFrameId(value ?? string.Empty);
                if (parsedId > uint.MaxValue)
                {
                    id = 0;
                    return false;
                }

                id = (uint)parsedId;
                return id > 0;
            }
            catch
            {
                id = 0;
                return false;
            }
        }
    }

    private void AddFrame(string direction, CanFrame frame)
    {
        RunOnUi(() =>
        {
            var row = new CanFrameRow(direction, frame);
            _frameHistory.Add(row);
            while (_frameHistory.Count > MaxFrameHistoryEntries)
            {
                _frameHistory.RemoveAt(0);
            }

            if ((IsFrameCapturePaused && string.Equals(direction, "RX", StringComparison.Ordinal))
                || !FrameMatchesFilter(frame))
            {
                return;
            }

            Frames.Add(row);
            while (Frames.Count > 1000)
            {
                Frames.RemoveAt(0);
            }
        });
    }

    private bool FrameMatchesFilter(CanFrame frame)
    {
        if (!IsFrameFilterEnabled)
        {
            return true;
        }

        if (SelectedProject is null)
        {
            return true;
        }

        var projectIds = new[]
        {
            SelectedProject.PhysicalRequestId,
            SelectedProject.FunctionalRequestId,
            SelectedProject.ResponseAddressId
        };

        var hasValidProjectId = false;
        foreach (var projectId in projectIds)
        {
            if (TryParseConfiguredCanId(projectId, out var parsedProjectId))
            {
                hasValidProjectId = true;
                if (frame.Id == parsedProjectId)
                {
                    return true;
                }
            }
        }

        return !hasValidProjectId;
    }

    private void AppendLog(string message)
    {
        var entry = new SystemLogEntry(DateTime.Now, ClassifyStatusText(message), message);
        RunOnUi(() =>
        {
            LogEntries.Add(entry);
            RecentLogEntries.Add(entry);
            LatestLogEntry = entry;

            while (LogEntries.Count > MaxLogEntries)
            {
                LogEntries.RemoveAt(0);
            }

            while (RecentLogEntries.Count > MaxRecentLogEntries)
            {
                RecentLogEntries.RemoveAt(0);
            }

            ApplyLogFilters();
            RaiseLogSummaryProperties();
        });
        WriteLogLine(entry.Text);
    }

    private void ApplyLogFilters()
    {
        FilteredLogEntries.Clear();
        foreach (var entry in LogEntries.Where(LogEntryMatchesFilter))
        {
            FilteredLogEntries.Add(entry);
        }

        OnPropertyChanged(nameof(LogFilterSummaryText));
    }

    private bool LogEntryMatchesFilter(SystemLogEntry entry)
    {
        if (!string.Equals(SelectedLogLevelFilter, "全部", StringComparison.Ordinal)
            && !string.Equals(entry.LevelText, SelectedLogLevelFilter, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(LogSearchText)
            && !entry.Text.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SelectedLogTimeRangeFilter switch
        {
            "最近1小时" => entry.Timestamp >= DateTime.Now.AddHours(-1),
            "今天" => entry.Timestamp.Date == DateTime.Today,
            "最近24小时" => entry.Timestamp >= DateTime.Now.AddDays(-1),
            "最近7天" => entry.Timestamp >= DateTime.Now.AddDays(-7),
            _ => true
        };
    }

    private void SetStatus(string text, DiagnosticStatusKind kind)
    {
        StatusText = text;
        StatusKind = kind;
    }

    /// <summary>
    /// Reports an exception raised by a command handler. The handler is invoked from
    /// <see cref="CommandErrorHandler"/> so a failing command surfaces in the status
    /// bar and log instead of terminating the process.
    /// </summary>
    private void HandleCommandException(Exception exception)
    {
        var summary = $"{exception.GetType().Name}: {exception.Message}";
        RunOnUi(() =>
        {
            SetStatus("操作失败", DiagnosticStatusKind.Error);
            AppendLog($"命令执行失败 - {summary}");
        });

        // The in-memory entry stays readable; the full stack goes to the file log only.
        WriteLogLine($"--- 命令异常 ---{Environment.NewLine}{exception}");
    }

    private static DiagnosticStatusKind ClassifyStatusText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || ContainsAny(text, "ready", "idle", "disconnected", "未开始", "断开"))
        {
            return DiagnosticStatusKind.Neutral;
        }

        if (ContainsAny(text, "failed", "failure", "error", "timeout", "timed out", "nrc", "negative response", "失败", "错误", "超时", "不通过"))
        {
            return DiagnosticStatusKind.Error;
        }

        if (ContainsAny(text, "cancel", "canceled", "cancelled", "skip", "warning", "取消", "跳过", "告警", "警告"))
        {
            return DiagnosticStatusKind.Warning;
        }

        if (ContainsAny(text, "running", "flashing", "download", "executing", "pending", "step ", "执行中", "下载中", "运行", "检测中", "等待"))
        {
            return DiagnosticStatusKind.Running;
        }

        if (ContainsAny(text, "success", "succeeded", "completed", "saved", "connected", "ok", "pass", "完成", "已连接", "通过", "保存"))
        {
            return DiagnosticStatusKind.Success;
        }

        return DiagnosticStatusKind.Neutral;
    }

    private static bool ContainsAny(string value, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // Queued rather than blocking: the CAN receive loop and flash callbacks run on
        // background threads, and they must never wait for the UI thread. A blocking
        // Invoke deadlocks as soon as the UI thread is busy or shutting down.
        // Callers on the UI thread still take the synchronous fast path above.
        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
        {
            // The dispatcher is shutting down, so UI updates are no longer possible.
        }
    }

    private void WriteLogLine(string line)
    {
        if (!LogFileEnabled || string.IsNullOrWhiteSpace(LogFilePath))
        {
            return;
        }

        try
        {
            var path = ResolveRuntimePath(LogFilePath);
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            RotateLogFileIfNeeded(path);
            File.AppendAllText(path, line + Environment.NewLine);
            CleanupExpiredLogFiles(path);
        }
        catch
        {
            // The in-memory log remains authoritative if file logging is unavailable.
        }
    }

    private string ResolveRuntimePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(_paths.RootDirectory, path);
    }

    private string ToRuntimeRelativePath(string path)
    {
        try
        {
            var root = Path.GetFullPath(_paths.RootDirectory);
            var fullPath = Path.GetFullPath(path);
            var relativePath = Path.GetRelativePath(root, fullPath);

            if (!Path.IsPathRooted(relativePath)
                && relativePath != ".."
                && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return relativePath;
            }
        }
        catch
        {
            // Keep the absolute path if it cannot be safely relativized.
        }

        return path;
    }

    private string GetCompactPathDisplay(string path)
    {
        try
        {
            var root = Path.GetFullPath(_paths.RootDirectory);
            var fullPath = Path.GetFullPath(path);
            var relativePath = Path.GetRelativePath(root, fullPath);

            if (!Path.IsPathRooted(relativePath)
                && relativePath != ".."
                && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return relativePath;
            }

            var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? fullPath : $"...{Path.DirectorySeparatorChar}{name}";
        }
        catch
        {
            return path;
        }
    }

    private void RotateLogFileIfNeeded(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var maxBytes = (long)LogMaxFileSizeMb * 1024 * 1024;
        if (new FileInfo(path).Length < maxBytes)
        {
            return;
        }

        File.Move(path, CreateRotatedLogFilePath(path));
    }

    private void CleanupExpiredLogFiles(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-LogRetentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, GetRotatedLogSearchPattern(path)))
        {
            if (File.GetLastWriteTime(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }

    private static string CreateRotatedLogFilePath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = GetLogFileExtension(path);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var rotatedPath = Path.Combine(directory, $"{fileName}_{timestamp}{extension}");

        for (var index = 1; File.Exists(rotatedPath); index++)
        {
            rotatedPath = Path.Combine(directory, $"{fileName}_{timestamp}_{index}{extension}");
        }

        return rotatedPath;
    }

    private static string GetRotatedLogSearchPattern(string path)
    {
        return $"{Path.GetFileNameWithoutExtension(path)}_*{GetLogFileExtension(path)}";
    }

    private static string GetLogFileExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? ".log" : extension;
    }

    private static int NormalizeLogRetentionDays(int? days)
    {
        return Math.Clamp(days.GetValueOrDefault(DefaultLogRetentionDays), MinimumLogRetentionDays, MaximumLogRetentionDays);
    }

    private static int NormalizeLogMaxFileSizeMb(int? sizeMb)
    {
        return Math.Clamp(sizeMb.GetValueOrDefault(DefaultLogMaxFileSizeMb), MinimumLogMaxFileSizeMb, MaximumLogMaxFileSizeMb);
    }

    private static void CopyFileIfDifferent(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath) || string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, true);
    }

    private static void CopyDirectoryIfDifferent(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory) || string.Equals(
                Path.GetFullPath(sourceDirectory),
                Path.GetFullPath(destinationDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourceFile, destinationPath, true);
        }
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ToggleConnectionCommand.RaiseCanExecuteChanged();
        StartFlashCommand.RaiseCanExecuteChanged();
        CancelFlashCommand.RaiseCanExecuteChanged();
        DeleteProjectCommand.RaiseCanExecuteChanged();
        SaveProjectsCommand.RaiseCanExecuteChanged();
        AddProjectCommand.RaiseCanExecuteChanged();
        NewProjectCommand.RaiseCanExecuteChanged();
        RaiseProjectEditorCommandStates();
        LoadFlowCommand.RaiseCanExecuteChanged();
        SaveFlowCommand.RaiseCanExecuteChanged();
        NewFlowCommand.RaiseCanExecuteChanged();
        RaiseAddFlowStepCommandStates();
        ReplaceFlowStepTemplateCommand.RaiseCanExecuteChanged();
        DeleteFlowStepCommand.RaiseCanExecuteChanged();
        MoveFlowStepUpCommand.RaiseCanExecuteChanged();
        MoveFlowStepDownCommand.RaiseCanExecuteChanged();
        SendManualFrameCommand.RaiseCanExecuteChanged();
    }

    private void RaiseAddFlowStepCommandStates()
    {
        ConfirmAddFlowStepCommand.RaiseCanExecuteChanged();
        CancelAddFlowStepCommand.RaiseCanExecuteChanged();
    }

    private void RaiseProjectEditorCommandStates()
    {
        EditProjectCommand.RaiseCanExecuteChanged();
        SaveProjectEditorCommand.RaiseCanExecuteChanged();
        CancelProjectEditorCommand.RaiseCanExecuteChanged();
        DeleteProjectEditorCommand.RaiseCanExecuteChanged();
    }

    private void RaiseLogSummaryProperties()
    {
        OnPropertyChanged(nameof(HasLogEntries));
        OnPropertyChanged(nameof(LatestLogText));
        OnPropertyChanged(nameof(LogEntryCountText));
        OnPropertyChanged(nameof(LogFilterSummaryText));
        OnPropertyChanged(nameof(DownloadInfoText));
    }

    private void RaiseDeviceStatusProperties()
    {
        OnPropertyChanged(nameof(DeviceStatusText));
        OnPropertyChanged(nameof(DeviceStatusKind));
        OnPropertyChanged(nameof(DeviceStatusIcon));
        OnPropertyChanged(nameof(ConnectionSummaryText));
    }

    private sealed class AppSettingsSnapshot
    {
        public bool KeepRunningInTray { get; set; }
        public bool AutoFlashEnabled { get; set; }
        public bool AutoSearchBaudRate { get; set; } = true;
        public bool LogFileEnabled { get; set; } = true;
        public string? LogFilePath { get; set; }
        public int? LogRetentionDays { get; set; }
        public int? LogMaxFileSizeMb { get; set; }
        public bool LogAutoScrollEnabled { get; set; } = true;
        public string? SelectedDeviceType { get; set; }
        public string? SelectedBaudRate { get; set; }
        public string? SelectedCanChannel { get; set; }
        public string? ProjectConfigPath { get; set; }
        public string? FlowConfigDirectory { get; set; }
        public string? FormulaDatabaseDirectory { get; set; }
    }
}
