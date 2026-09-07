using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
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

public sealed class MainViewModel : ObservableObject
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

    private static readonly IReadOnlyDictionary<string, string> LightThemeBrushes = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F6F8FB",
        ["PanelBrush"] = "#FFFFFF",
        ["SurfaceBrush"] = "#F8FAFC",
        ["CardBorderBrush"] = "#E5EAF1",
        ["DividerBrush"] = "#E5EAF1",
        ["InputBackgroundBrush"] = "#FFFFFF",
        ["InputBorderBrush"] = "#D7DEE8",
        ["PrimarySoftBorderBrush"] = "#B8CAE5",
        ["PrimaryBrush"] = "#3568B8",
        ["PrimaryActiveBrush"] = "#2E5B9E",
        ["PrimaryStrongBrush"] = "#274D86",
        ["PrimarySoftBrush"] = "#EDF3FC",
        ["PrimaryPressedBrush"] = "#DCE7F6",
        ["BrandNavigationBrush"] = "#3F67A3",
        ["TextBrush"] = "#334155",
        ["CardTitleTextBrush"] = "#3B4A5F",
        ["BodyTextBrush"] = "#475569",
        ["LabelTextBrush"] = "#64748B",
        ["SubtleTextBrush"] = "#8A97A8",
        ["DisabledTextBrush"] = "#B6C0CC",
        ["InverseTextBrush"] = "#FFFFFF",
        ["IconDefaultBrush"] = "#64748B",
        ["IconPrimaryBrush"] = "#3568B8",
        ["IconSuccessBrush"] = "#16A34A",
        ["IconWarningBrush"] = "#D97706",
        ["IconErrorBrush"] = "#DC2626",
        ["IconMutedBrush"] = "#94A3B8",
        ["SidebarBrush"] = "#182335",
        ["SidebarHoverBrush"] = "#202D42",
        ["SidebarActiveBrush"] = "#4D3F67A3",
        ["SidebarActiveRailBrush"] = "#6F95C8",
        ["SidebarActiveIconBrush"] = "#C6D5E8",
        ["SidebarTextBrush"] = "#A8B3C4",
        ["SidebarMutedTextBrush"] = "#7F8CA1",
        ["TableHeaderBrush"] = "#F8FAFC",
        ["TableAltRowBrush"] = "#F8FAFC",
        ["ProgressTrackBrush"] = "#E5EAF1",
        ["LogPanelBrush"] = "#0F172A",
        ["LogHoverBrush"] = "#111827",
        ["LogSelectedBrush"] = "#1E293B",
        ["LogTextBrush"] = "#CBD5E1",
        ["SuccessBrush"] = "#16A34A",
        ["StatusSuccessBrush"] = "#16A34A",
        ["StatusSuccessSoftBrush"] = "#DCFCE7",
        ["StatusSuccessBorderBrush"] = "#86EFAC",
        ["StatusRunningBrush"] = "#3568B8",
        ["StatusRunningSoftBrush"] = "#EDF3FC",
        ["StatusRunningBorderBrush"] = "#6F95C8",
        ["StatusWarningBrush"] = "#D97706",
        ["StatusWarningSoftBrush"] = "#FEF3C7",
        ["StatusWarningBorderBrush"] = "#FCD34D",
        ["StatusErrorBrush"] = "#DC2626",
        ["StatusErrorSoftBrush"] = "#FEE2E2",
        ["StatusErrorBorderBrush"] = "#FCA5A5",
        ["StatusNeutralBrush"] = "#94A3B8",
        ["StatusNeutralSoftBrush"] = "#F6F8FB",
        ["StatusNeutralBorderBrush"] = "#D7DEE8",
        ["DangerBrush"] = "#D97706",
        ["DangerActiveBrush"] = "#B45309",
        ["DangerPressedBrush"] = "#92400E",
        ["DangerBorderBrush"] = "#FCD34D",
        ["DangerTextBrush"] = "#1E293B",
        ["NeutralSoftBrush"] = "#F6F8FB"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkThemeBrushes = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#0F172A",
        ["PanelBrush"] = "#111827",
        ["SurfaceBrush"] = "#1F2937",
        ["CardBorderBrush"] = "#334155",
        ["DividerBrush"] = "#334155",
        ["InputBackgroundBrush"] = "#111827",
        ["InputBorderBrush"] = "#475569",
        ["PrimarySoftBorderBrush"] = "#2563EB",
        ["PrimaryBrush"] = "#3B82F6",
        ["PrimaryActiveBrush"] = "#60A5FA",
        ["PrimaryStrongBrush"] = "#2563EB",
        ["PrimarySoftBrush"] = "#1E3A5F",
        ["PrimaryPressedBrush"] = "#1D4ED8",
        ["BrandNavigationBrush"] = "#2563EB",
        ["TextBrush"] = "#E5E7EB",
        ["CardTitleTextBrush"] = "#F1F5F9",
        ["BodyTextBrush"] = "#CBD5E1",
        ["LabelTextBrush"] = "#94A3B8",
        ["SubtleTextBrush"] = "#94A3B8",
        ["DisabledTextBrush"] = "#64748B",
        ["InverseTextBrush"] = "#FFFFFF",
        ["IconDefaultBrush"] = "#94A3B8",
        ["IconPrimaryBrush"] = "#93C5FD",
        ["IconSuccessBrush"] = "#22C55E",
        ["IconWarningBrush"] = "#F59E0B",
        ["IconErrorBrush"] = "#F87171",
        ["IconMutedBrush"] = "#64748B",
        ["SidebarBrush"] = "#0B1220",
        ["SidebarHoverBrush"] = "#1E293B",
        ["SidebarActiveBrush"] = "#334155",
        ["SidebarActiveRailBrush"] = "#60A5FA",
        ["SidebarActiveIconBrush"] = "#BFDBFE",
        ["SidebarTextBrush"] = "#CBD5E1",
        ["SidebarMutedTextBrush"] = "#94A3B8",
        ["TableHeaderBrush"] = "#1F2937",
        ["TableAltRowBrush"] = "#111827",
        ["ProgressTrackBrush"] = "#334155",
        ["LogPanelBrush"] = "#020617",
        ["LogHoverBrush"] = "#0F172A",
        ["LogSelectedBrush"] = "#1E293B",
        ["LogTextBrush"] = "#CBD5E1",
        ["SuccessBrush"] = "#22C55E",
        ["StatusSuccessBrush"] = "#22C55E",
        ["StatusSuccessSoftBrush"] = "#052E16",
        ["StatusSuccessBorderBrush"] = "#15803D",
        ["StatusRunningBrush"] = "#60A5FA",
        ["StatusRunningSoftBrush"] = "#172554",
        ["StatusRunningBorderBrush"] = "#2563EB",
        ["StatusWarningBrush"] = "#F59E0B",
        ["StatusWarningSoftBrush"] = "#451A03",
        ["StatusWarningBorderBrush"] = "#D97706",
        ["StatusErrorBrush"] = "#F87171",
        ["StatusErrorSoftBrush"] = "#450A0A",
        ["StatusErrorBorderBrush"] = "#B91C1C",
        ["StatusNeutralBrush"] = "#94A3B8",
        ["StatusNeutralSoftBrush"] = "#1F2937",
        ["StatusNeutralBorderBrush"] = "#334155",
        ["DangerBrush"] = "#F59E0B",
        ["DangerActiveBrush"] = "#D97706",
        ["DangerPressedBrush"] = "#B45309",
        ["DangerBorderBrush"] = "#FCD34D",
        ["DangerTextBrush"] = "#111827",
        ["NeutralSoftBrush"] = "#1F2937"
    };

    private readonly AppConfigurationPaths _paths = new();
    private readonly JsonProjectConfigRepository _projectRepository;
    private readonly JsonBootConfigRepository _bootConfigRepository;
    private readonly FirmwareLoader _firmwareLoader = new();
    private readonly CanDeviceFactory _canDeviceFactory = new();
    private ICanDevice? _canDevice;
    private BootConfig? _loadedFlowConfig;
    private ProjectConfigEntry? _selectedProject;
    private ProjectConfigEntry? _editingProject;
    private FlowStepEditorRow? _selectedFlowStep;
    private string? _selectedBootConfig;
    private string _selectedDeviceType = "Mock";
    private string _selectedBaudRate = "500K";
    private string _driverFilePath = string.Empty;
    private string _applicationFilePath = string.Empty;
    private string _manualFrameId = "7E0";
    private string _manualFrameData = "02 10 03";
    private string _selectedFunctionCheckConfig = "mock";
    private string _selectedCanChannel = "0";
    private string _logFilePath = DefaultLogFilePath;
    private string _selectedLogLevelFilter = "全部";
    private string _selectedLogTimeRangeFilter = "全部";
    private string _logSearchText = string.Empty;
    private bool _nightModeEnabled;
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
    private bool _isConnected;
    private bool _isBusy;
    private int _selectedShellIndex;
    private int _progress;
    private int _logRetentionDays = DefaultLogRetentionDays;
    private int _logMaxFileSizeMb = DefaultLogMaxFileSizeMb;
    private string _statusText = "Ready";
    private DiagnosticStatusKind _statusKind = DiagnosticStatusKind.Neutral;
    private DiagnosticStatusKind _downloadStatusKind = DiagnosticStatusKind.Neutral;
    private string _flowValidationText = "No flow loaded.";
    private string _projectEditorTitle = "新建项目";
    private string _adminPassword = string.Empty;
    private string _adminPasswordMessage = "请输入管理员密码，本次启动内有效。";
    private SystemLogEntry? _latestLogEntry;
    private readonly List<CanFrameRow> _frameHistory = [];

    public MainViewModel()
    {
        LoadAppSettings();
        ApplyNightMode(NightModeEnabled);
        _projectRepository = new JsonProjectConfigRepository(_paths);
        _bootConfigRepository = new JsonBootConfigRepository(_paths);

        RefreshCommand = new RelayCommand(Refresh);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected && !IsBusy);
        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => !IsBusy);
        BrowseDriverCommand = new RelayCommand(() => BrowseFirmware(path => DriverFilePath = path));
        BrowseApplicationCommand = new RelayCommand(() => BrowseFirmware(path => ApplicationFilePath = path));
        StartFlashCommand = new AsyncRelayCommand(StartFlashAsync, () => IsConnected && SelectedProject is not null && !IsBusy);
        RefreshLogCommand = new RelayCommand(ApplyLogFilters);
        ClearLogCommand = new RelayCommand(ClearLogs);
        OpenLogFileCommand = new RelayCommand(OpenLogFile);
        ExportLogCommand = new RelayCommand(ExportLog);
        ClearFramesCommand = new RelayCommand(ClearFrames);
        ToggleFrameCaptureCommand = new RelayCommand(ToggleFrameCapture);
        ToggleFrameFilterCommand = new RelayCommand(ToggleFrameFilter);
        ExportFramesCommand = new RelayCommand(ExportFrames);
        AddProjectCommand = new RelayCommand(AddProject);
        DeleteProjectCommand = new RelayCommand(DeleteSelectedProject, () => SelectedProject is not null);
        SaveProjectsCommand = new RelayCommand(SaveProjects);
        RefreshProjectsCommand = new RelayCommand(RefreshProjectList);
        NewProjectCommand = new RelayCommand(OpenNewProjectEditor);
        EditProjectCommand = new RelayCommand(EditSelectedProject, () => SelectedProject is not null);
        SaveProjectEditorCommand = new RelayCommand(SaveProjectEditor, () => IsProjectEditorVisible);
        CancelProjectEditorCommand = new RelayCommand(CloseProjectEditor, () => IsProjectEditorVisible);
        DeleteProjectEditorCommand = new RelayCommand(DeleteProjectEditor, () => IsEditingProject);
        BrowseProjectEditorDriverCommand = new RelayCommand(() => BrowseFirmware(path => ProjectEditor.DriverFilePath = path));
        BrowseProjectEditorApplicationCommand = new RelayCommand(() => BrowseFirmware(path => ProjectEditor.ApplicationFilePath = path));
        LoadFlowCommand = new RelayCommand(LoadFlowFromSelected, () => !string.IsNullOrWhiteSpace(SelectedBootConfig));
        SaveFlowCommand = new RelayCommand(SaveFlowConfig, () => !string.IsNullOrWhiteSpace(SelectedBootConfig) && FlowRows.Count > 0);
        ValidateFlowCommand = new RelayCommand(() => ValidateFlowConfig());
        AddFlowStepCommand = new RelayCommand(AddFlowStep);
        DeleteFlowStepCommand = new RelayCommand(DeleteSelectedFlowStep, () => SelectedFlowStep is not null);
        MoveFlowStepUpCommand = new RelayCommand(() => MoveSelectedFlowStep(-1), () => SelectedFlowStep is not null);
        MoveFlowStepDownCommand = new RelayCommand(() => MoveSelectedFlowStep(1), () => SelectedFlowStep is not null);
        SendManualFrameCommand = new AsyncRelayCommand(SendManualFrameAsync, () => IsConnected && !IsBusy);
        StartFunctionCheckCommand = new AsyncRelayCommand(StartFunctionCheckAsync, () => !IsBusy);
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
        OpenRuntimeLogPageCommand = new RelayCommand(() => SelectedShellIndex = 9);
        ShowAdminPasswordCommand = new RelayCommand(ShowAdminPasswordPrompt);
        SubmitAdminPasswordCommand = new RelayCommand(SubmitAdminPassword);
        CancelAdminPasswordCommand = new RelayCommand(CancelAdminPasswordPrompt);

        LoadDefaultFunctionChecks();
        Refresh();
    }

    public ObservableCollection<ProjectConfigEntry> Projects { get; } = [];
    public ProjectEditorViewModel ProjectEditor { get; } = new();
    public ObservableCollection<string> BootConfigFiles { get; } = [];
    public ObservableCollection<string> DeviceTypes { get; } = ["Mock", "ZLG USBCAN-2A (4)"];
    public ObservableCollection<string> BaudRates { get; } = ["250K", "500K", "1000K"];
    public ObservableCollection<string> CanChannels { get; } = ["0", "1"];
    public ObservableCollection<string> FunctionCheckConfigs { get; } = ["mock"];
    public ObservableCollection<string> LogLevelFilters { get; } = ["全部", "普通", "成功", "运行", "警告", "错误"];
    public ObservableCollection<string> LogTimeRangeFilters { get; } = ["全部", "最近1小时", "今天", "最近24小时", "最近7天"];
    public ObservableCollection<SystemLogEntry> LogEntries { get; } = [];
    public ObservableCollection<SystemLogEntry> FilteredLogEntries { get; } = [];
    public ObservableCollection<SystemLogEntry> RecentLogEntries { get; } = [];
    public ObservableCollection<CanFrameRow> Frames { get; } = [];
    public ObservableCollection<FlowStepEditorRow> FlowRows { get; } = [];
    public ObservableCollection<FunctionalCheckRow> FunctionalChecks { get; } = [];
    public ObservableCollection<AlgorithmConfigRow> AlgorithmRows { get; } = [];

    public RelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand ToggleConnectionCommand { get; }
    public RelayCommand BrowseDriverCommand { get; }
    public RelayCommand BrowseApplicationCommand { get; }
    public AsyncRelayCommand StartFlashCommand { get; }
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
    public RelayCommand AddFlowStepCommand { get; }
    public RelayCommand DeleteFlowStepCommand { get; }
    public RelayCommand MoveFlowStepUpCommand { get; }
    public RelayCommand MoveFlowStepDownCommand { get; }
    public AsyncRelayCommand SendManualFrameCommand { get; }
    public AsyncRelayCommand StartFunctionCheckCommand { get; }
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
    public RelayCommand ShowAdminPasswordCommand { get; }
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
            if (SetProperty(ref _selectedProject, value))
            {
                ApplySelectedProject();
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

    public string? SelectedBootConfig
    {
        get => _selectedBootConfig;
        set
        {
            if (SetProperty(ref _selectedBootConfig, value))
            {
                LoadFlowFromSelected();
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

    public string SelectedFunctionCheckConfig
    {
        get => _selectedFunctionCheckConfig;
        set => SetProperty(ref _selectedFunctionCheckConfig, value);
    }

    public string SelectedCanChannel
    {
        get => _selectedCanChannel;
        set
        {
            if (SetProperty(ref _selectedCanChannel, value))
            {
                SaveAppSettings();
            }
        }
    }

    public bool NightModeEnabled
    {
        get => _nightModeEnabled;
        set
        {
            if (SetProperty(ref _nightModeEnabled, value))
            {
                ApplyNightMode(value);
                OnPropertyChanged(nameof(PageModeStatusText));
                SaveAppSettings();
                AppendLog($"Night mode {(value ? "enabled" : "disabled")}.");
            }
        }
    }

    public string PageModeStatusText => NightModeEnabled ? "深色页面已启用" : "浅色页面已启用";

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
                OnPropertyChanged(nameof(StatusBarContextText));
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
    public string DatabaseStatusText => "数据库已连接";
    public string ScriptRootText => Path.Combine(_paths.ConfigDirectory, "Scripts");
    public string AlgorithmCatalogText => AlgorithmRows.Count == 0 ? "未发现算法配置" : $"已发现 {AlgorithmRows.Count} 个算法配置";
    public string StatusBarContextText => SelectedShellIndex switch
    {
        0 => "刷写中心 / 固件刷写",
        1 => "刷写中心 / 刷写监控",
        2 => "刷写中心 / 历史记录",
        3 => "诊断测试 / 功能检测",
        4 => "配置管理 / 项目配置",
        5 => "配置管理 / 流程配置",
        6 => "配置管理 / 算法配置",
        7 => "系统 / 系统设置",
        8 => "系统 / 开发者选项",
        9 => "系统 / 日志",
        _ => DatabaseStatusText
    };

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

    private void ShowAdminPasswordPrompt()
    {
        if (AdminModeEnabled)
        {
            return;
        }

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

    private void Refresh()
    {
        var selectedName = SelectedProject?.ProjectName;
        Projects.Clear();
        foreach (var project in _projectRepository.LoadAll())
        {
            Projects.Add(project);
        }

        BootConfigFiles.Clear();
        foreach (var config in _bootConfigRepository.ListConfigFiles())
        {
            BootConfigFiles.Add(config);
        }

        LoadAlgorithmCatalog();

        SelectedProject = Projects.FirstOrDefault(project => string.Equals(project.ProjectName, selectedName, StringComparison.OrdinalIgnoreCase))
            ?? Projects.FirstOrDefault();
        SelectedBootConfig ??= SelectedProject?.BootConfigFile ?? BootConfigFiles.FirstOrDefault();
        LoadFlowFromSelected();
        OnPropertyChanged(nameof(ConfigRootText));
        OnPropertyChanged(nameof(ScriptRootText));
        OnPropertyChanged(nameof(FlowConfigDirectory));
        OnPropertyChanged(nameof(FormulaDatabaseDirectory));
        OnPropertyChanged(nameof(ProjectConfigPath));
        OnPropertyChanged(nameof(FlowConfigDirectoryDisplay));
        OnPropertyChanged(nameof(FormulaDatabaseDirectoryDisplay));
        OnPropertyChanged(nameof(ProjectConfigPathDisplay));
        AppendLog($"Configuration loaded: projects={Projects.Count}, boot={BootConfigFiles.Count}");
    }

    private void RefreshProjectList()
    {
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

            _nightModeEnabled = settings.NightModeEnabled;
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
                NightModeEnabled = NightModeEnabled,
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
            _paths.BootConfigDirectoryOverride = path;
            SaveAppSettings();
            Refresh();
        });
    }

    private void ChooseFormulaDatabaseDirectory()
    {
        BrowseFolder("选择配方数据库目录", FormulaDatabaseDirectory, path =>
        {
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

    private static void ApplyNightMode(bool enabled)
    {
        if (Application.Current is null)
        {
            return;
        }

        var palette = enabled ? DarkThemeBrushes : LightThemeBrushes;

        foreach (var (key, value) in palette)
        {
            var color = ToColor(value);
            if (!TryUpdateBrushResource(Application.Current.Resources, key, color))
            {
                Application.Current.Resources[key] = new SolidColorBrush(color);
            }
        }

        foreach (Window window in Application.Current.Windows)
        {
            window.InvalidateVisual();
        }
    }

    private static bool TryUpdateBrushResource(ResourceDictionary dictionary, string key, Color color)
    {
        var updated = false;

        if (dictionary.Contains(key))
        {
            if (dictionary[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
            }
            else
            {
                dictionary[key] = new SolidColorBrush(color);
            }

            updated = true;
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            if (TryUpdateBrushResource(mergedDictionary, key, color))
            {
                updated = true;
            }
        }

        return updated;
    }

    private static Color ToColor(string value)
    {
        return (Color?)ColorConverter.ConvertFromString(value) ?? Colors.Transparent;
    }

    private void LoadAlgorithmCatalog()
    {
        AlgorithmRows.Clear();
        AlgorithmRows.Add(new AlgorithmConfigRow("内置安全算法", "AES128_OneFunc", "DiagnosticFlashTool.Core", "可用"));

        AddAlgorithmScripts("安全算法脚本", Path.Combine(ScriptRootText, "Security"));
        AddAlgorithmScripts("CRC 算法脚本", Path.Combine(ScriptRootText, "CRC"));
        OnPropertyChanged(nameof(AlgorithmCatalogText));
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
            var firmwareSet = LoadFirmwareSet(SelectedProject);

            var options = new DiagnosticTransportOptions
            {
                PhysicalRequestId = HexUtil.ParseUInt32(SelectedProject.PhysicalRequestId),
                FunctionalRequestId = HexUtil.ParseUInt32(SelectedProject.FunctionalRequestId),
                ResponseId = HexUtil.ParseUInt32(SelectedProject.ResponseAddressId),
                Channel = 0
            };

            using var transport = new IsoTpTransport(_canDevice, options);
            var udsClient = new UdsClient(transport);
            var executor = new FlashFlowExecutor(udsClient);
            var progress = new Progress<FlashProgress>(item =>
            {
                Progress = Math.Clamp(item.Percent, 0, 100);
                DownloadStatusKind = DiagnosticStatusKind.Running;
                SetStatus(item.Message, DiagnosticStatusKind.Running);
            });

            var result = await executor.ExecuteAsync(
                bootConfig,
                SelectedProject,
                firmwareSet,
                progress,
                AppendLog,
                cancellationToken);

            DownloadStatusKind = result.Success ? DiagnosticStatusKind.Success : DiagnosticStatusKind.Error;
            var resultMessage = result.Success
                ? "Flash completed"
                : string.IsNullOrWhiteSpace(result.UserMessage) ? "Flash failed" : result.UserMessage;
            SetStatus(resultMessage, DownloadStatusKind);
            Progress = result.Success ? 100 : Progress;
        }
        catch (OperationCanceledException)
        {
            DownloadStatusKind = DiagnosticStatusKind.Warning;
            SetStatus("Flash canceled", DiagnosticStatusKind.Warning);
            AppendLog("Flash canceled.");
        }
        catch (Exception ex)
        {
            DownloadStatusKind = DiagnosticStatusKind.Error;
            SetStatus(ex.Message, DiagnosticStatusKind.Error);
            AppendLog($"Flash failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartFunctionCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            SetStatus("功能检测中", DiagnosticStatusKind.Running);
            AppendLog($"Function check started: {SelectedFunctionCheckConfig}");

            foreach (var row in FunctionalChecks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(120, cancellationToken);
                row.MarkCompleted(row.ExpectedValue, DateTime.Now);
            }

            SetStatus("功能检测完成", DiagnosticStatusKind.Success);
            AppendLog($"Function check completed: {FunctionalChecks.Count} items.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("功能检测取消", DiagnosticStatusKind.Warning);
            AppendLog("Function check canceled.");
        }
        catch (Exception ex)
        {
            SetStatus("功能检测失败", DiagnosticStatusKind.Error);
            AppendLog($"Function check failed: {ex.Message}");
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

    private FirmwareSet LoadFirmwareSet(ProjectConfigEntry project)
    {
        FirmwareImage? driver = null;
        FirmwareImage? application = null;

        var driverPath = string.IsNullOrWhiteSpace(DriverFilePath) ? project.DriveFilePath : DriverFilePath;
        if (!string.IsNullOrWhiteSpace(driverPath) && File.Exists(driverPath))
        {
            driver = _firmwareLoader.Load(driverPath, FirmwareImageKind.Driver);
            AppendLog($"Driver image loaded: {Path.GetFileName(driverPath)}, {driver.Length} bytes");
        }

        var appPath = string.IsNullOrWhiteSpace(ApplicationFilePath) ? project.FlashFilePath : ApplicationFilePath;
        if (!string.IsNullOrWhiteSpace(appPath) && File.Exists(appPath))
        {
            var fallbackAddress = HexUtil.ParseUInt32(project.AppStartAddress, 0);
            application = _firmwareLoader.Load(appPath, FirmwareImageKind.Application, fallbackAddress);
            AppendLog($"Application image loaded: {Path.GetFileName(appPath)}, {application.Length} bytes");
        }

        return new FirmwareSet { Driver = driver, Application = application };
    }

    private void LoadDefaultFunctionChecks()
    {
        FunctionalChecks.Clear();
        FunctionalChecks.Add(new FunctionalCheckRow(1, "供电电压检测", "6"));
        FunctionalChecks.Add(new FunctionalCheckRow(2, "系统电流检测", "10"));
        FunctionalChecks.Add(new FunctionalCheckRow(3, "温度传感器", "15"));
        FunctionalChecks.Add(new FunctionalCheckRow(4, "状态寄存器", "3"));
        FunctionalChecks.Add(new FunctionalCheckRow(5, "故障码检测", "8"));
        FunctionalChecks.Add(new FunctionalCheckRow(6, "通信状态检测", "12"));
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
        if (SelectedProject is null)
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

    private void LoadFlowFromSelected()
    {
        FlowRows.Clear();
        _loadedFlowConfig = null;

        if (string.IsNullOrWhiteSpace(SelectedBootConfig))
        {
            FlowValidationText = "No BOOT config selected.";
            return;
        }

        try
        {
            _loadedFlowConfig = _bootConfigRepository.Load(SelectedBootConfig);
            foreach (var step in _loadedFlowConfig.Flow.OrderBy(step => step.Id))
            {
                FlowRows.Add(FlowStepEditorRow.FromConfig(step));
            }

            SelectedFlowStep = FlowRows.FirstOrDefault();
            ValidateFlowConfig();
            AppendLog($"Flow loaded: {SelectedBootConfig}, steps={FlowRows.Count}");
        }
        catch (Exception ex)
        {
            FlowValidationText = ex.Message;
            AppendLog($"Flow load failed: {ex.Message}");
        }
    }

    private void SaveFlowConfig()
    {
        if (string.IsNullOrWhiteSpace(SelectedBootConfig))
        {
            return;
        }

        if (!ValidateFlowConfig())
        {
            SetStatus("Flow validation failed", DiagnosticStatusKind.Error);
            return;
        }

        var config = _loadedFlowConfig ?? _bootConfigRepository.Load(SelectedBootConfig);
        config.Flow = FlowRows.OrderBy(row => row.Id).Select(row => row.ToConfig()).ToList();
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            config.Name = Path.GetFileNameWithoutExtension(SelectedBootConfig);
        }

        _bootConfigRepository.Save(SelectedBootConfig, config);
        _loadedFlowConfig = config;
        SetStatus("Flow saved", DiagnosticStatusKind.Success);
        AppendLog($"Flow saved: {SelectedBootConfig}, steps={FlowRows.Count}");
    }

    private bool ValidateFlowConfig()
    {
        var issues = new List<string>();
        if (FlowRows.Count == 0)
        {
            issues.Add("Flow has no steps.");
        }

        issues.AddRange(FlowRows.GroupBy(row => row.Id)
            .Where(group => group.Count() > 1)
            .Select(group => $"Duplicate step id: {group.Key}"));

        foreach (var row in FlowRows)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                issues.Add($"Step {row.Id}: name is empty.");
            }

            var isDownload = string.Equals(row.StepType, "DownloadDriver", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.StepType, "DownloadApplication", StringComparison.OrdinalIgnoreCase);
            if (!isDownload && string.IsNullOrWhiteSpace(row.Service))
            {
                issues.Add($"Step {row.Id}: service is empty.");
            }

            if (!string.IsNullOrWhiteSpace(row.Service) && !HexUtil.TryParseByte(row.Service, out _))
            {
                issues.Add($"Step {row.Id}: service is not a byte.");
            }

            if (!string.IsNullOrWhiteSpace(row.SubService) && !HexUtil.TryParseByte(row.SubService, out _))
            {
                issues.Add($"Step {row.Id}: sub-service is not a byte.");
            }
        }

        FlowValidationText = issues.Count == 0 ? $"OK: {FlowRows.Count} flow steps." : string.Join(Environment.NewLine, issues);
        return issues.Count == 0;
    }

    private void AddFlowStep()
    {
        var nextId = FlowRows.Count == 0 ? 1 : FlowRows.Max(row => row.Id) + 1;
        var row = new FlowStepEditorRow
        {
            Id = nextId,
            Name = $"New Step {nextId}",
            AddressingMode = "physical",
            TimeoutMs = "1500",
            PendingTimeoutMs = "30000"
        };

        FlowRows.Add(row);
        SelectedFlowStep = row;
        ValidateFlowConfig();
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

        FlowRows.Move(index, target);
        for (var i = 0; i < FlowRows.Count; i++)
        {
            FlowRows[i].Id = i + 1;
        }

        ValidateFlowConfig();
    }

    private void ApplySelectedProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        SelectedBootConfig = string.IsNullOrWhiteSpace(SelectedProject.BootConfigFile)
            ? BootConfigFiles.FirstOrDefault()
            : SelectedProject.BootConfigFile;
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
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
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
        DeleteProjectCommand.RaiseCanExecuteChanged();
        RaiseProjectEditorCommandStates();
        LoadFlowCommand.RaiseCanExecuteChanged();
        SaveFlowCommand.RaiseCanExecuteChanged();
        DeleteFlowStepCommand.RaiseCanExecuteChanged();
        MoveFlowStepUpCommand.RaiseCanExecuteChanged();
        MoveFlowStepDownCommand.RaiseCanExecuteChanged();
        SendManualFrameCommand.RaiseCanExecuteChanged();
        StartFunctionCheckCommand.RaiseCanExecuteChanged();
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
        public bool NightModeEnabled { get; set; }
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
