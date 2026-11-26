using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using DiagnosticFlashTool.App.Utilities;
using DiagnosticFlashTool.App.Views;
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

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string LegacyAppSettingsFilePath = Path.Combine(
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
    private const int MaxDownloadInfoEntries = 30;
    private const int MaxFrameHistoryEntries = 5000;
    private const uint MaxStandardCanId = 0x7FF;
    private const uint MaxExtendedCanId = 0x1FFFFFFF;
    private const string AutoBaudRateModeText = "自动搜索";
    private const string FixedBaudRateModeText = "指定波特率";
    private const string ProjectBaudRateModeText = "跟随项目";

    private readonly AppConfigurationPaths _paths;
    private readonly JsonApplicationDataLocationRepository _applicationDataLocationRepository;
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
    private string _selectedBaudRateMode = AutoBaudRateModeText;
    private string _selectedCanFdDataBaudRate = "2M";
    private bool _useBitRateSwitch = true;
    private CanDeviceInfo? _detectedCanDeviceInfo;
    private string _deviceProbeStatusText = "尚未检测设备";
    private string _driverFilePath = string.Empty;
    private string _applicationFilePath = string.Empty;
    private string _manualFrameId = "7E0";
    private string _manualFrameData = "02 10 03";
    private bool? _isManualFrameExtended;
    private string _selectedCanChannel = "0";
    private string _logFilePath = DefaultLogFilePath;
    private string _selectedLogLevelFilter = "全部";
    private string _selectedLogTimeRangeFilter = "全部";
    private string _logSearchText = string.Empty;
    private bool _keepRunningInTray;
    private bool _logFileEnabled = true;
    private bool _logAutoScrollEnabled = true;
    private bool _isFrameCapturePaused;
    private bool _isFrameFilterEnabled;
    private bool _isNewProjectDialogVisible;
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
    private readonly Stopwatch _flashStopwatch = new();
    private DateTimeOffset? _flashStartedAt;
    private int _logRetentionDays = DefaultLogRetentionDays;
    private int _logMaxFileSizeMb = DefaultLogMaxFileSizeMb;
    private string _statusText = "Ready";
    private DiagnosticStatusKind _statusKind = DiagnosticStatusKind.Neutral;
    private DiagnosticStatusKind _downloadStatusKind = DiagnosticStatusKind.Neutral;
    private string _flowValidationText = "未加载流程配置。";
    private bool _hasFlowValidationResult;
    private IReadOnlyList<ProjectFlowStep> _projectFlowSteps = [];
    private string _projectFlowNotice = "请在侧栏选择刷写项目。";
    private int? _currentExecutingStepId;
    private string _newFlowProjectName = string.Empty;
    private string? _selectedFlowProjectTemplate;
    private string _newFlowProjectValidationText = string.Empty;
    private SystemLogEntry? _latestLogEntry;
    private readonly List<CanFrameRow> _frameHistory = [];

    public MainViewModel() : this(new AppConfigurationPaths())
    {
    }

    public MainViewModel(AppConfigurationPaths paths)
    {
        _paths = paths;
        CommandErrorHandler.Current = HandleCommandException;
        _applicationDataLocationRepository = new JsonApplicationDataLocationRepository(_paths);
        LoadAppSettings();
        InitializeApplicationData();
        _projectRepository = new JsonProjectConfigRepository(_paths);
        _bootConfigRepository = new JsonBootConfigRepository(_paths);
        ProjectEditor.PropertyChanged += OnProjectEditorPropertyChanged;

        RefreshCommand = new RelayCommand(RefreshWithConfirmation, () => !IsBusy);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected && !IsBusy);
        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => !IsBusy);
        ProbeCanDeviceCommand = new AsyncRelayCommand(ProbeCanDeviceAsync, () => !IsConnected && !IsBusy);
        BrowseDriverCommand = new RelayCommand(() => BrowseFirmware(path => DriverFilePath = path), () => CanChangeWorkspace);
        BrowseApplicationCommand = new RelayCommand(() => BrowseFirmware(path => ApplicationFilePath = path), () => CanChangeWorkspace);
        StartFlashCommand = new AsyncRelayCommand(
            StartFlashAsync,
            () => IsConnected
                && ActiveFlashConfiguration is not null
                && !string.IsNullOrWhiteSpace(SelectedBootConfig)
                && (IsProjectWorkspace || IsSystemAdministrator)
                && !IsProjectFlowDraft
                && !IsBusy);
        CancelFlashCommand = new RelayCommand(() => StartFlashCommand.Cancel(), () => StartFlashCommand.IsRunning);
        StartFlashCommand.CanExecuteChanged += (_, _) => CancelFlashCommand.RaiseCanExecuteChanged();
        ClearLogCommand = new RelayCommand(ClearLogs);
        OpenLogFileCommand = new RelayCommand(OpenLogFile, () => LogFileEnabled && IsSystemAdministrator);
        ExportLogCommand = new RelayCommand(ExportLog);
        ClearFramesCommand = new RelayCommand(ClearFrames, () => IsSystemAdministrator);
        ToggleFrameCaptureCommand = new RelayCommand(ToggleFrameCapture, () => IsSystemAdministrator);
        ToggleFrameFilterCommand = new RelayCommand(ToggleFrameFilter, () => IsSystemAdministrator);
        ExportFramesCommand = new RelayCommand(ExportFrames, () => IsSystemAdministrator);
        AddProjectCommand = AdministratorCommand(AddProject, () => _draftFlowProject is null);
        DeleteProjectCommand = AdministratorCommand(
            DeleteSelectedProject,
            () => _draftFlowProject is null && SelectedProject is not null);
        SaveProjectsCommand = AdministratorCommand(SaveProjects, () => _draftFlowProject is null);
        RefreshProjectsCommand = new RelayCommand(RefreshProjectList, () => !IsBusy);
        NewProjectCommand = AdministratorCommand(
            OpenNewProjectEditor, () => _draftFlowProject is null && !IsNewProjectDialogVisible);
        EditProjectCommand = new RelayCommand(
            EditSelectedProject,
            () => CanChangeWorkspace && _draftFlowProject is null
                && SelectedProject is not null && !IsNewProjectDialogVisible);
        SaveProjectEditorCommand = new RelayCommand(
            SaveProjectEditor,
            () => CanChangeWorkspace && _draftFlowProject is null && IsProjectEditorVisible
                && (IsSystemAdministrator || IsProjectWorkspace && IsEditingProject));
        CancelProjectEditorCommand = new RelayCommand(CloseProjectEditor, () => IsProjectEditorVisible);
        DeleteProjectEditorCommand = AdministratorCommand(
            DeleteProjectEditor,
            () => _draftFlowProject is null && IsEditingProject);
        BrowseProjectEditorDriverCommand = new RelayCommand(() => BrowseFirmware(path => ProjectEditor.DriverFilePath = path));
        BrowseProjectEditorApplicationCommand = new RelayCommand(() => BrowseFirmware(path => ProjectEditor.ApplicationFilePath = path));
        LoadFlowCommand = AdministratorCommand(
            ReloadFlowFromSelected,
            () => !string.IsNullOrWhiteSpace(SelectedBootConfig)
                && !IsProjectFlowDraft);
        SaveFlowCommand = FlowEditorCommand(SaveFlowConfig, () => !string.IsNullOrWhiteSpace(SelectedBootConfig) && FlowRows.Count > 0);
        ValidateFlowCommand = AdministratorCommand(() =>
        {
            if (!NavigateToUdsPage(4))
            {
                return;
            }
            var passed = ValidateFlowConfig();
            AppendLog(passed
                ? $"流程校验通过，共 {FlowRows.Count} 个节点。"
                : "流程校验失败，请在流程配置页查看详细结果。");
        });
        NewFlowCommand = FlowEditorCommand(OpenNewFlowProjectDialog);
        ConfirmNewFlowProjectCommand = FlowEditorCommand(
            ConfirmNewFlowProject,
            () => IsNewFlowProjectDialogVisible
                && !string.IsNullOrWhiteSpace(NewFlowProjectName)
                && (!CreateFlowProjectFromTemplate
                    || !string.IsNullOrWhiteSpace(SelectedFlowProjectTemplate)));
        CancelNewFlowProjectCommand = new RelayCommand(
            CloseNewFlowProjectDialog,
            () => IsNewFlowProjectDialogVisible);
        AddFlowStepCommand = FlowEditorCommand(OpenAddFlowStepDialog);
        ReplaceFlowStepTemplateCommand = FlowEditorCommand(
            OpenReplaceFlowStepDialog,
            () => SelectedFlowStep is not null);
        ConfirmAddFlowStepCommand = FlowEditorCommand(
            ConfirmSelectedFlowStepTemplate,
            () => IsAddFlowStepDialogVisible
                && SelectedFlowStepTemplate is not null
                && (!IsReplacingFlowStepTemplate || SelectedFlowStep is not null));
        CancelAddFlowStepCommand = new RelayCommand(
            CloseAddFlowStepDialog,
            () => IsAddFlowStepDialogVisible);
        DeleteFlowStepCommand = FlowEditorCommand(DeleteSelectedFlowStep, () => SelectedFlowStep is not null);
        MoveFlowStepUpCommand = FlowEditorCommand(() => MoveSelectedFlowStep(-1), () => SelectedFlowStep is not null);
        MoveFlowStepDownCommand = FlowEditorCommand(() => MoveSelectedFlowStep(1), () => SelectedFlowStep is not null);
        SendManualFrameCommand = new AsyncRelayCommand(SendManualFrameAsync, () => IsSystemAdministrator && IsConnected && !IsBusy);
        ImportBuiltinConfigurationCommand = AdministratorCommand(ImportBuiltinConfiguration);
        ClearRuntimeConfigurationCommand = AdministratorCommand(ClearRuntimeConfiguration);
        OpenApplicationDataDirectoryCommand = new RelayCommand(() => OpenPathLocation(ApplicationDataDirectory));
        ChooseApplicationDataDirectoryCommand = new AsyncRelayCommand(ChooseApplicationDataDirectoryAsync, () => IsSystemAdministrator && !IsBusy);
        OpenFlowConfigDirectoryCommand = new RelayCommand(() => OpenPathLocation(FlowConfigDirectory));
        ChooseFlowConfigDirectoryCommand = AdministratorCommand(ChooseFlowConfigDirectory);
        OpenProjectConfigDirectoryCommand = new RelayCommand(() => OpenPathLocation(ProjectConfigPath));
        ChooseProjectConfigFileCommand = AdministratorCommand(ChooseProjectConfigFile);
        OpenLogDirectoryCommand = new RelayCommand(() => OpenPathLocation(LogFilePath), () => IsSystemAdministrator);
        ChooseLogFileCommand = new RelayCommand(ChooseLogFile);
        RestoreDefaultLogStorageCommand = new RelayCommand(RestoreDefaultLogStorage);
        NavigateToSystemLogCommand = new RelayCommand(() => NavigateToUdsPage(2), () => CanChangeWorkspace);
        NavigateToLogSettingsCommand = new RelayCommand(() => SelectedShellIndex = 6);
        InitializeNavigation();
        Refresh();
    }

    public ObservableCollection<ProjectConfigEntry> Projects { get; } = [];
    public ProjectEditorViewModel ProjectEditor { get; } = new();
    public ObservableCollection<string> BootConfigFiles { get; } = [];
    public ObservableCollection<string> DeviceTypes { get; } = ["Mock", "ZLGCAN"];
    public ObservableCollection<string> BaudRates { get; } = ["250K", "500K", "1000K"];
    public ObservableCollection<string> CanFdDataBaudRates { get; } = ["1M", "2M", "4M", "5M", "8M"];
    public ObservableCollection<string> BaudRateModes { get; } = [AutoBaudRateModeText, FixedBaudRateModeText, ProjectBaudRateModeText];
    public ObservableCollection<string> CanProtocols { get; } = ["CAN", "CANFD"];
    public ObservableCollection<string> CanChannels { get; } = ["0", "1"];
    public ObservableCollection<string> LogLevelFilters { get; } = ["全部", "普通", "成功", "运行", "警告", "错误"];
    public ObservableCollection<string> LogTimeRangeFilters { get; } = ["全部", "最近1小时", "今天", "最近24小时", "最近7天"];
    public ObservableCollection<SystemLogEntry> LogEntries { get; } = [];
    public ObservableCollection<SystemLogEntry> FilteredLogEntries { get; } = [];
    public ObservableCollection<SystemLogEntry> DownloadInfoEntries { get; } = [];
    public ObservableCollection<CanFrameRow> Frames { get; } = [];
    public ObservableCollection<FlowStepEditorRow> FlowRows { get; } = [];
    public IReadOnlyList<FlowStepTemplate> FlowStepTemplates { get; } = FlowStepTemplate.BuiltIn;
    public ObservableCollection<string> FlowSecurityAlgorithmOptions { get; } = [string.Empty, "AES128_OneFunc"];
    public ObservableCollection<string> FlowCrcAlgorithmOptions { get; } = [string.Empty, "CRC16_DNP", "CRC32"];
    public ObservableCollection<AlgorithmConfigRow> AlgorithmRows { get; } = [];

    public RelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand ToggleConnectionCommand { get; }
    public AsyncRelayCommand ProbeCanDeviceCommand { get; }
    public RelayCommand BrowseDriverCommand { get; }
    public RelayCommand BrowseApplicationCommand { get; }
    public AsyncRelayCommand StartFlashCommand { get; }
    public RelayCommand CancelFlashCommand { get; }
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
    public RelayCommand OpenApplicationDataDirectoryCommand { get; }
    public AsyncRelayCommand ChooseApplicationDataDirectoryCommand { get; }
    public RelayCommand OpenFlowConfigDirectoryCommand { get; }
    public RelayCommand ChooseFlowConfigDirectoryCommand { get; }
    public RelayCommand OpenProjectConfigDirectoryCommand { get; }
    public RelayCommand ChooseProjectConfigFileCommand { get; }
    public RelayCommand OpenLogDirectoryCommand { get; }
    public RelayCommand ChooseLogFileCommand { get; }
    public RelayCommand RestoreDefaultLogStorageCommand { get; }
    public RelayCommand NavigateToSystemLogCommand { get; }
    public RelayCommand NavigateToLogSettingsCommand { get; }
    public bool IsProjectEditorVisible => IsEditingProject || IsNewProjectDialogVisible;
    public bool IsNewProjectDialogVisible => _isNewProjectDialogVisible;
    public bool IsEditingProject => _editingProject is not null;
    public bool CanDeleteEditedProject => IsEditingProject && IsSystemAdministrator;

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
                ConfirmNewFlowProjectCommand.RaiseCanExecuteChanged();
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
                ConfirmNewFlowProjectCommand.RaiseCanExecuteChanged();
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
                ConfirmNewFlowProjectCommand.RaiseCanExecuteChanged();
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

    public ProjectConfigEntry? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (ReferenceEquals(_selectedProject, value))
            {
                return;
            }

            if (!_suppressFlowSelectionPrompt && (IsBusy || StartFlashCommand.IsRunning))
            {
                OnPropertyChanged(nameof(SelectedProject));
                return;
            }

            if (!_suppressFlowSelectionPrompt
                && HasUnsavedFlowChanges()
                && !ConfirmUnsavedFlowChanges("切换配置", selectFallbackWhenDiscardingDraft: false))
            {
                OnPropertyChanged(nameof(SelectedProject));
                return;
            }

            var previousProjectName = _selectedProject?.ProjectName;
            if (SetProperty(ref _selectedProject, value))
            {
                if (IsEditingProject)
                {
                    CloseProjectEditor();
                }

                if (!string.Equals(previousProjectName, value?.ProjectName, StringComparison.OrdinalIgnoreCase))
                {
                    ResetFlashProgress();
                }

                ApplySelectedProject();
                RefreshWorkspaceState();
                OnPropertyChanged(nameof(StatusBarProjectText));
                OnPropertyChanged(nameof(FlashPageProjectIdText));
                OnPropertyChanged(nameof(IsCanFdRequested));
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
                OnPropertyChanged(nameof(FlowConfigFileStatusKind));
                OnPropertyChanged(nameof(FlowConfigFileDisplay));
                OnPropertyChanged(nameof(StatusBarProjectText));
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
                DetectedCanDeviceInfo = null;
                DeviceProbeStatusText = "设备类型已更改，请重新检测";
                ResetCanChannels(2);
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

    public string SelectedBaudRateMode
    {
        get => _selectedBaudRateMode;
        set
        {
            var normalized = BaudRateModes.Contains(value) ? value : AutoBaudRateModeText;
            if (SetProperty(ref _selectedBaudRateMode, normalized))
            {
                if (normalized == ProjectBaudRateModeText && ActiveFlashConfiguration is not null)
                {
                    SelectedBaudRate = ActiveFlashConfiguration.BaudRate;
                }

                OnPropertyChanged(nameof(IsFixedBaudRateMode));
                SaveAppSettings();
                RaiseConnectionDetailProperties();
            }
        }
    }

    public bool IsFixedBaudRateMode => SelectedBaudRateMode == FixedBaudRateModeText;
    public bool IsCanFdRequested => CanProtocolExtensions.Parse(ActiveFlashConfiguration?.CanProtocol) == CanProtocol.CanFd;

    public string SelectedCanFdDataBaudRate
    {
        get => _selectedCanFdDataBaudRate;
        set
        {
            if (SetProperty(ref _selectedCanFdDataBaudRate, value))
            {
                SaveAppSettings();
                RaiseConnectionDetailProperties();
            }
        }
    }

    public bool UseBitRateSwitch
    {
        get => _useBitRateSwitch;
        set
        {
            if (SetProperty(ref _useBitRateSwitch, value))
            {
                SaveAppSettings();
                OnPropertyChanged(nameof(BitRateSwitchStatusText));
            }
        }
    }

    public CanDeviceInfo? DetectedCanDeviceInfo
    {
        get => _detectedCanDeviceInfo;
        private set
        {
            if (SetProperty(ref _detectedCanDeviceInfo, value))
            {
                OnPropertyChanged(nameof(DetectedDeviceText));
                OnPropertyChanged(nameof(CanFdCapabilityText));
                RaiseConnectionDetailProperties();
            }
        }
    }

    public string DeviceProbeStatusText
    {
        get => _deviceProbeStatusText;
        private set => SetProperty(ref _deviceProbeStatusText, value);
    }

    public string DetectedDeviceText => DetectedCanDeviceInfo is null
        ? "未识别"
        : string.IsNullOrWhiteSpace(DetectedCanDeviceInfo.SerialNumber)
            ? $"{DetectedCanDeviceInfo.Model} / {DetectedCanDeviceInfo.ChannelCount} 通道"
            : $"{DetectedCanDeviceInfo.Model} / S/N {DetectedCanDeviceInfo.SerialNumber} / {DetectedCanDeviceInfo.ChannelCount} 通道";

    public string CanFdCapabilityText => DetectedCanDeviceInfo is null
        ? "检测设备后显示能力"
        : DetectedCanDeviceInfo.SupportsCanFd ? "支持 CAN FD" : "仅支持经典 CAN";

    public string RequestedProtocolText => CanProtocolExtensions.Parse(ActiveFlashConfiguration?.CanProtocol).ToConfigValue();

    public string ActiveProtocolText => IsConnected && _canDevice is not null
        ? _canDevice.ActiveProtocol.ToConfigValue()
        : RequestedProtocolText;

    public string ProtocolStatusText => IsConnected && _canDevice is not null
        ? _canDevice.ActiveProtocol == CanProtocolExtensions.Parse(ActiveFlashConfiguration?.CanProtocol)
            ? ActiveProtocolText
            : $"{ActiveProtocolText}（设备不支持 CAN FD，已回退）"
        : $"待连接：{RequestedProtocolText}";

    public string BitRateSwitchStatusText => UseBitRateSwitch ? "已开启 BRS" : "未开启 BRS";

    public string DriverFilePath
    {
        get => _driverFilePath;
        set
        {
            if (SetProperty(ref _driverFilePath, value))
            {
                OnPropertyChanged(nameof(DriverFileDisplay));
            }
        }
    }

    public string ApplicationFilePath
    {
        get => _applicationFilePath;
        set
        {
            if (SetProperty(ref _applicationFilePath, value))
            {
                OnPropertyChanged(nameof(ApplicationFileDisplay));
            }
        }
    }

    public string ManualFrameId
    {
        get => _manualFrameId;
        set
        {
            if (SetProperty(ref _manualFrameId, value) && _isManualFrameExtended is null)
            {
                OnPropertyChanged(nameof(IsManualFrameExtended));
            }
        }
    }

    public bool IsManualFrameExtended
    {
        get => _isManualFrameExtended ?? (ulong.TryParse(
            GetManualFrameIdDigits(ManualFrameId), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
            && id > MaxStandardCanId);
        set => SetProperty(ref _isManualFrameExtended, (bool?)value);
    }

    public string ManualFrameData
    {
        get => _manualFrameData;
        set
        {
            if (SetProperty(ref _manualFrameData, value))
            {
                OnPropertyChanged(nameof(ManualFrameByteCountText));
            }
        }
    }

    public string ManualFrameByteCountText
    {
        get
        {
            try
            {
                var count = HexUtil.ParseBytes(ManualFrameData).Length;
                return count == 1 ? "1 Byte" : $"{count} Bytes";
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                return "无效数据";
            }
        }
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

    public bool LogFileEnabled
    {
        get => _logFileEnabled;
        set
        {
            if (SetProperty(ref _logFileEnabled, value))
            {
                OnPropertyChanged(nameof(LogFileStatusText));
                OnPropertyChanged(nameof(LogFileStatusKind));
                OnPropertyChanged(nameof(LogFilePathDisplay));
                OpenLogFileCommand.RaiseCanExecuteChanged();
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
                OnPropertyChanged(nameof(LogFileStatusText));
                OnPropertyChanged(nameof(LogFileStatusKind));
                OnPropertyChanged(nameof(LogFilePathDisplay));
                OnPropertyChanged(nameof(LogFilePathFullPath));
                SaveAppSettings();
            }
        }
    }

    public string LogFileStatusText => !LogFileEnabled
        ? "文件日志：未开启"
        : LogFileStatusKind == DiagnosticStatusKind.Warning
            ? "文件日志：路径不存在"
            : "文件日志：已开启";

    public DiagnosticStatusKind LogFileStatusKind => !LogFileEnabled
        ? DiagnosticStatusKind.Neutral
        : IsLogFileLocationAvailable(ResolveRuntimePath(LogFilePath))
            ? DiagnosticStatusKind.Success
            : DiagnosticStatusKind.Warning;

    public string LogFileStatusPathText => FilePathDisplayFormatter.GetFileName(LogFilePathFullPath);

    public string LogFilePathFullPath => ResolveRuntimePath(LogFilePath);

    public string LogFilePathDisplay => ApplyPathStatus(
        FilePathDisplayFormatter.GetFileName(LogFilePathFullPath),
        LogFileStatusKind);

    private string AppSettingsFilePath => _paths.SettingsFilePath;

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

    public string LogFilterSummaryText => $"显示 {FilteredLogEntries.Count} / {WorkspaceLogEntries.Count} 条";

    public bool IsLogListEmpty => FilteredLogEntries.Count == 0;

    public string LogEmptyStateText => WorkspaceLogEntries.Count == 0
        ? "暂无操作日志"
        : "没有符合筛选条件的日志";

    public string LogEmptyStateHintText => WorkspaceLogEntries.Count == 0
        ? "连接设备、加载配置或执行刷写后，运行记录将显示在这里。"
        : "调整级别、关键词或时间范围后再试。";

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

    public bool ShowDownloadInfoSources => DownloadInfoEntries.Any(entry =>
        !string.Equals(entry.ProjectName, SelectedProject?.ProjectName, StringComparison.OrdinalIgnoreCase));

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

    public int SelectedShellIndex
    {
        get => _selectedShellIndex;
        set
        {
            if (IsProjectWorkspace && value is >= 1 and <= 5)
            {
                if (!NavigateToUdsPage(value))
                {
                    OnPropertyChanged();
                }
                return;
            }

            if ((value != 6 && !CanChangeWorkspace && value != _selectedShellIndex)
                || !CanAccessPage(value, projectWorkspace: IsProjectWorkspace && value != 6))
            {
                OnPropertyChanged(nameof(SelectedShellIndex));
                return;
            }

            if (SetProperty(ref _selectedShellIndex, value))
            {
                CloseProjectEditor();
                CloseNewFlowProjectDialog();
                CloseAddFlowStepDialog();
            }
            SyncNavigationSelection();
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
                RaiseConnectionDetailProperties();
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
                OnPropertyChanged(nameof(CanChangeWorkspace));
                RaiseCommandStates();
                RefreshNavigationCommands();
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

    public ObservableCollection<FlowValidationLine> FlowValidationLines { get; } = [];

    public IReadOnlyList<ProjectFlowStep> ProjectFlowSteps
    {
        get => _projectFlowSteps;
        private set => SetProperty(ref _projectFlowSteps, value);
    }

    public string ProjectFlowNotice
    {
        get => _projectFlowNotice;
        private set => SetProperty(ref _projectFlowNotice, value);
    }

    public bool HasFlowValidationResult => _hasFlowValidationResult;

    public int FlowValidationErrorCount => FlowValidationLines.Count(line => line.IsError);

    public int FlowValidationWarningCount => FlowValidationLines.Count(line => line.IsWarning);

    public DiagnosticStatusKind FlowValidationStateKind => !_hasFlowValidationResult
        ? DiagnosticStatusKind.Neutral
        : FlowValidationErrorCount > 0
            ? DiagnosticStatusKind.Error
            : FlowValidationWarningCount > 0 ? DiagnosticStatusKind.Warning : DiagnosticStatusKind.Success;

    public string FlowValidationStatusText => !_hasFlowValidationResult
        ? "尚未校验"
        : FlowValidationErrorCount > 0
            ? $"{FlowValidationErrorCount} 项错误"
            : FlowValidationWarningCount > 0 ? $"{FlowValidationWarningCount} 项提示" : "校验通过";

    public string FlowValidationSummaryText => _hasFlowValidationResult
        ? $"共 {FlowRows.Count} 个流程节点"
        : "点击「校验」检查当前流程配置";

    public int? CurrentExecutingStepId
    {
        get => _currentExecutingStepId;
        private set
        {
            if (SetProperty(ref _currentExecutingStepId, value))
            {
                UpdateExecutingFlowRow();
            }
        }
    }

    public string ConnectionText => IsConnected ? $"已连接：{ConnectedDeviceText} / {SelectedBaudRate}" : "未连接";
    public string BaudRateStatusText => IsConnected
        ? $"{SelectedBaudRate} / {SelectedBaudRateMode}"
        : SelectedBaudRateMode == AutoBaudRateModeText ? "待自动搜索" : $"{SelectedBaudRate} / {SelectedBaudRateMode}";
    public string ConnectionSummaryText => IsConnected
        ? $"{ConnectedDeviceText} / {ActiveProtocolText} / {SelectedBaudRate}"
        : IsConnectionFailure ? "连接失败，请重试" : "等待设备连接";
    public string ConnectedDeviceText => _canDevice?.DeviceInfo?.Model
        ?? DetectedCanDeviceInfo?.Model
        ?? SelectedDeviceType;
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
    public string FlashStartTimeText => _flashStartedAt?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "—";

    public string FlashDurationText
    {
        get
        {
            if (_flashStartedAt is null)
            {
                return "—";
            }

            var elapsed = _flashStopwatch.Elapsed;
            return string.Create(CultureInfo.InvariantCulture,
                $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
        }
    }

    public string ScriptRootText => _paths.ScriptDirectory;
    public string AlgorithmCatalogText => AlgorithmRows.Count == 0 ? "未发现算法配置" : $"已发现 {AlgorithmRows.Count} 个算法配置";

    public string FlashPageProjectIdText => WorkspaceDescription;

    /// <summary>状态栏运行态：当前 CAN 设备与通道。</summary>
    public string StatusBarDeviceText => IsConnected
        ? $"设备：{ConnectedDeviceText} / {ActiveProtocolText} / {SelectedBaudRate} / 通道 {SelectedCanChannel}"
        : "设备：未连接";

    /// <summary>状态栏运行态：当前项目与 BOOT 配置。</summary>
    public string StatusBarProjectText => !IsProjectWorkspace
        ? $"{WorkspaceDescription} / BOOT：{FilePathDisplayFormatter.GetFileName(SelectedBootConfig)}"
        : SelectedProject is null
        ? "项目：未选择"
        : string.IsNullOrWhiteSpace(SelectedBootConfig)
            ? $"项目：{SelectedProject.ProjectName}"
            : $"项目：{SelectedProject.ProjectName} / BOOT：{FilePathDisplayFormatter.GetFileName(SelectedBootConfig)}";

    /// <summary>状态栏运行态：当前下载/刷写状态。</summary>
    public string StatusBarStateText => $"状态：{DownloadStatusText}";

    public SystemLogEntry? LatestLogEntry
    {
        get => _latestLogEntry;
        private set => SetProperty(ref _latestLogEntry, value);
    }

    public bool HasLogEntries => WorkspaceLogEntries.Count > 0;
    public string LatestLogText => LatestLogEntry?.Text ?? "暂无日志";
    public string LogEntryCountText => HasLogEntries ? $"已记录 {WorkspaceLogEntries.Count} 条" : "暂无日志";

    public string ConfigRootText => _paths.ConfigDirectory;
    public string ApplicationDataDirectory => _paths.ApplicationDataDirectory;
    public DiagnosticStatusKind ApplicationDataDirectoryStatusKind => Directory.Exists(ApplicationDataDirectory)
        ? DiagnosticStatusKind.Neutral
        : DiagnosticStatusKind.Warning;
    public string ApplicationDataDirectoryDisplay => ApplyPathStatus(
        ApplicationDataDirectory,
        ApplicationDataDirectoryStatusKind);
    public string FlowConfigDirectory => _paths.BootConfigDirectory;
    public string FlowConfigDirectoryDisplay => ApplyPathStatus(
        FlowConfigDirectory,
        FlowConfigDirectoryStatusKind);
    public string FlowConfigFilePath => string.IsNullOrWhiteSpace(SelectedBootConfig)
        ? string.Empty
        : Path.IsPathRooted(SelectedBootConfig)
            ? SelectedBootConfig
            : Path.Combine(FlowConfigDirectory, SelectedBootConfig);
    public string ProjectConfigPath => _paths.ProjectConfigPath;
    public DiagnosticStatusKind FlowConfigDirectoryStatusKind => Directory.Exists(FlowConfigDirectory)
        ? DiagnosticStatusKind.Neutral
        : DiagnosticStatusKind.Warning;
    public DiagnosticStatusKind FlowConfigFileStatusKind => !string.IsNullOrWhiteSpace(FlowConfigFilePath)
        && File.Exists(FlowConfigFilePath)
            ? DiagnosticStatusKind.Neutral
            : DiagnosticStatusKind.Warning;
    public DiagnosticStatusKind ProjectConfigPathStatusKind => File.Exists(ProjectConfigPath)
        ? DiagnosticStatusKind.Neutral
        : DiagnosticStatusKind.Warning;
    public string FlowConfigFileDisplay => string.IsNullOrWhiteSpace(FlowConfigFilePath)
        ? "未选择流程配置文件"
        : ApplyPathStatus(FilePathDisplayFormatter.GetFileName(FlowConfigFilePath), FlowConfigFileStatusKind);
    public string ProjectConfigPathDisplay => ApplyPathStatus(
        FilePathDisplayFormatter.GetFileName(ProjectConfigPath),
        ProjectConfigPathStatusKind);

    private bool IsConnectionFailure => !IsConnected
        && StatusKind == DiagnosticStatusKind.Error
        && ContainsAny(StatusText, "connect failed", "connection failed", "连接失败");

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
            NormalizeProjectDefaults(project);
            Projects.Add(project);
        }

        BootConfigFiles.Clear();
        foreach (var config in _bootConfigRepository.ListConfigFileNames())
        {
            BootConfigFiles.Add(config);
        }
        RefreshProjectFileStatuses();
        OnPropertyChanged(nameof(HasBootConfigTemplates));

        LoadAlgorithmCatalog();

        _suppressFlowSelectionPrompt = true;
        try
        {
            SelectedProject = Projects.FirstOrDefault(project => string.Equals(project.ProjectName, selectedName, StringComparison.OrdinalIgnoreCase))
                ?? Projects.FirstOrDefault();
        }
        finally
        {
            _suppressFlowSelectionPrompt = false;
        }
        RefreshWorkspaceState();
        OnPropertyChanged(nameof(ConfigRootText));
        OnPropertyChanged(nameof(ApplicationDataDirectory));
        OnPropertyChanged(nameof(ApplicationDataDirectoryDisplay));
        OnPropertyChanged(nameof(ApplicationDataDirectoryStatusKind));
        OnPropertyChanged(nameof(ScriptRootText));
        OnPropertyChanged(nameof(FlowConfigDirectory));
        OnPropertyChanged(nameof(FlowConfigDirectoryDisplay));
        OnPropertyChanged(nameof(FlowConfigFilePath));
        OnPropertyChanged(nameof(FlowConfigFileDisplay));
        OnPropertyChanged(nameof(FlowConfigFileStatusKind));
        OnPropertyChanged(nameof(ProjectConfigPath));
        OnPropertyChanged(nameof(ProjectConfigPathDisplay));
        OnPropertyChanged(nameof(FlowConfigDirectoryStatusKind));
        OnPropertyChanged(nameof(ProjectConfigPathStatusKind));
        OnPropertyChanged(nameof(LogFileStatusText));
        OnPropertyChanged(nameof(LogFileStatusKind));
        OnPropertyChanged(nameof(LogFilePathDisplay));
        OnPropertyChanged(nameof(LogFilePathFullPath));
        AppendLog($"Configuration loaded: projects={Projects.Count}, boot={BootConfigFiles.Count}",
            SelectedProject?.ProjectName, $"已加载 {Projects.Count} 个项目、{BootConfigFiles.Count} 个 BOOT 配置");
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

    private void InitializeApplicationData()
    {
        try
        {
            InitializeApplicationData(_paths);
        }
        catch (Exception ex)
        {
            SetStatus("初始化应用数据目录失败", DiagnosticStatusKind.Error);
            AppendLog($"Initialize application data directory failed: {ex.Message}");
        }
    }

    private static void InitializeApplicationData(AppConfigurationPaths paths)
    {
        Directory.CreateDirectory(paths.ApplicationDataDirectory);

        // Only supply missing built-in files; an existing data directory keeps its own configuration.
        if (paths.ProjectConfigPathOverride is null)
        {
            CopyFileIfMissing(paths.BuiltInProjectConfigPath, paths.ProjectConfigPath);
        }

        if (paths.BootConfigDirectoryOverride is null)
        {
            CopyDirectoryMissing(paths.BuiltInBootConfigDirectory, paths.BootConfigDirectory);
        }

        CopyDirectoryMissing(paths.BuiltInScriptDirectory, paths.ScriptDirectory);
    }

    private void LoadAppSettings()
    {
        try
        {
            _paths.ConfigDirectoryOverride = _applicationDataLocationRepository.Load();
            var settingsFilePath = File.Exists(AppSettingsFilePath)
                ? AppSettingsFilePath
                : LegacyAppSettingsFilePath;
            if (!File.Exists(settingsFilePath))
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<AppSettingsSnapshot>(
                File.ReadAllText(settingsFilePath),
                SettingsJsonOptions);
            if (settings is null)
            {
                return;
            }

            _keepRunningInTray = settings.KeepRunningInTray;
            _selectedUserRole = settings.UserRole == AdministratorRoleText ? AdministratorRoleText : StandardRoleText;
            _selectedBaudRateMode = string.IsNullOrWhiteSpace(settings.BaudRateSelectionMode)
                ? settings.AutoSearchBaudRate is false ? FixedBaudRateModeText : AutoBaudRateModeText
                : ToBaudRateModeText(BaudRateSelectionModeExtensions.Parse(settings.BaudRateSelectionMode));
            _logFileEnabled = settings.LogFileEnabled;
            _logFilePath = string.IsNullOrWhiteSpace(settings.LogFilePath)
                ? _logFilePath
                : settings.LogFilePath;
            _logRetentionDays = NormalizeLogRetentionDays(settings.LogRetentionDays);
            _logMaxFileSizeMb = NormalizeLogMaxFileSizeMb(settings.LogMaxFileSizeMb);
            _logAutoScrollEnabled = settings.LogAutoScrollEnabled;
            _selectedDeviceType = NormalizeDeviceType(settings.SelectedDeviceType);
            _selectedBaudRate = string.IsNullOrWhiteSpace(settings.SelectedBaudRate)
                ? _selectedBaudRate
                : settings.SelectedBaudRate;
            _selectedCanChannel = string.IsNullOrWhiteSpace(settings.SelectedCanChannel)
                ? _selectedCanChannel
                : settings.SelectedCanChannel;
            _selectedCanFdDataBaudRate = string.IsNullOrWhiteSpace(settings.CanFdDataBaudRate)
                ? _selectedCanFdDataBaudRate
                : settings.CanFdDataBaudRate;
            _useBitRateSwitch = settings.UseBitRateSwitch ?? true;
            _paths.ProjectConfigPathOverride = EmptyToNull(settings.ProjectConfigPath);
            _paths.BootConfigDirectoryOverride = EmptyToNull(settings.FlowConfigDirectory);

            if (!string.Equals(settingsFilePath, AppSettingsFilePath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(_paths.ApplicationDataDirectory);
                SaveAppSettings();
            }
        }
        catch
        {
            // Ignore malformed local settings and continue with defaults.
        }
    }

    private void SaveAppSettings(bool throwOnError = false)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppSettingsFilePath)!);
            var settings = new AppSettingsSnapshot
            {
                KeepRunningInTray = KeepRunningInTray,
                UserRole = SelectedUserRole,
                AutoSearchBaudRate = SelectedBaudRateMode == AutoBaudRateModeText,
                BaudRateSelectionMode = GetBaudRateSelectionMode().ToConfigValue(),
                LogFileEnabled = LogFileEnabled,
                LogFilePath = LogFilePath,
                LogRetentionDays = LogRetentionDays,
                LogMaxFileSizeMb = LogMaxFileSizeMb,
                LogAutoScrollEnabled = LogAutoScrollEnabled,
                SelectedDeviceType = SelectedDeviceType,
                SelectedBaudRate = SelectedBaudRate,
                SelectedCanChannel = SelectedCanChannel,
                CanFdDataBaudRate = SelectedCanFdDataBaudRate,
                UseBitRateSwitch = UseBitRateSwitch,
                ProjectConfigPath = _paths.ProjectConfigPathOverride,
                FlowConfigDirectory = _paths.BootConfigDirectoryOverride
            };
            File.WriteAllText(AppSettingsFilePath, JsonSerializer.Serialize(settings, SettingsJsonOptions));
        }
        catch when (!throwOnError)
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
            CopyFileIfDifferent(_paths.BuiltInProjectConfigPath, _paths.ProjectConfigPath);
            CopyDirectoryIfDifferent(_paths.BuiltInBootConfigDirectory, _paths.BootConfigDirectory);
            CopyDirectoryIfDifferent(_paths.BuiltInScriptDirectory, _paths.ScriptDirectory);
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

    private async Task ChooseApplicationDataDirectoryAsync(CancellationToken cancellationToken)
    {
        var path = BrowseFolder("选择应用数据目录", ApplicationDataDirectory);
        if (path is null
            || string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(ApplicationDataDirectory), StringComparison.OrdinalIgnoreCase)
            || !ConfirmUnsavedFlowChanges("切换应用数据目录"))
        {
            return;
        }

        var previousDirectoryOverride = _paths.ConfigDirectoryOverride;
        var targetPaths = new AppConfigurationPaths
        {
            RootDirectory = _paths.RootDirectory,
            DefaultApplicationDataDirectory = _paths.DefaultApplicationDataDirectory,
            ConfigDirectoryOverride = path,
            ProjectConfigPathOverride = _paths.ProjectConfigPathOverride,
            BootConfigDirectoryOverride = _paths.BootConfigDirectoryOverride
        };
        var switched = false;
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                InitializeApplicationData(targetPaths);
                _ = new JsonProjectConfigRepository(targetPaths).LoadAll();
                _ = new JsonBootConfigRepository(targetPaths).ListConfigFileNames();
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Editing may have continued while the target directory was being prepared.
            if (!ConfirmUnsavedFlowChanges("切换应用数据目录"))
            {
                return;
            }

            _paths.ConfigDirectoryOverride = path;
            switched = true;
            SaveAppSettings(throwOnError: true);
            Refresh();
            _applicationDataLocationRepository.Save(path);
            CloseProjectEditor();
            SetStatus("应用数据目录已更改", DiagnosticStatusKind.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("已取消更改应用数据目录", DiagnosticStatusKind.Neutral);
        }
        catch (Exception ex)
        {
            if (switched)
            {
                _paths.ConfigDirectoryOverride = previousDirectoryOverride;
                Refresh();
            }

            SetStatus("更改应用数据目录失败，请检查目录权限或配置文件", DiagnosticStatusKind.Error);
            AppendLog($"Change application data directory failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ChooseFlowConfigDirectory()
    {
        var path = BrowseFolder("选择流程配置目录", FlowConfigDirectory);
        if (path is null || !ConfirmUnsavedFlowChanges("切换流程配置目录"))
        {
            return;
        }

        _paths.BootConfigDirectoryOverride = path;
        SaveAppSettings();
        Refresh();
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

    private string? BrowseFolder(string title, string currentPath)
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

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
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
        foreach (var entry in LogEntries.Where(LogEntryMatchesWorkspace).ToList())
        {
            LogEntries.Remove(entry);
        }
        ApplyLogFilters();
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
        var securityAlgorithms = new[] { string.Empty, "AES128_OneFunc" }
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
        foreach (var algorithm in securityAlgorithms)
        {
            FlowSecurityAlgorithmOptions.Add(algorithm);
        }

        var crcAlgorithms = new[] { string.Empty, "CRC16_DNP", "CRC32" }
            .Concat(FlowRows.Where(row => row.IsDownloadStep).Select(row => row.CrcAlgorithm))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Prepend(string.Empty)
            .ToList();

        FlowCrcAlgorithmOptions.Clear();
        foreach (var algorithm in crcAlgorithms)
        {
            FlowCrcAlgorithmOptions.Add(algorithm);
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

    private async Task ProbeCanDeviceAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            await ProbeCanDeviceCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            DeviceProbeStatusText = "设备检测已取消";
        }
        catch (Exception ex)
        {
            DetectedCanDeviceInfo = null;
            DeviceProbeStatusText = $"设备检测失败：{ex.Message}";
            SetStatus("设备检测失败", DiagnosticStatusKind.Error);
            AppendLog(DeviceProbeStatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ProbeCanDeviceCoreAsync(CancellationToken cancellationToken)
    {
        DeviceProbeStatusText = "正在检测 CAN 设备…";
        var probe = _canDeviceFactory.CreateProbe(SelectedDeviceType);
        var info = await probe.ProbeAsync(SelectedDeviceType, cancellationToken: cancellationToken);
        if (info is null)
        {
            DetectedCanDeviceInfo = null;
            DeviceProbeStatusText = "未检测到可用设备";
            throw new InvalidOperationException("未检测到可用的 CAN 设备，请检查硬件连接和驱动。 ");
        }

        ApplyDetectedDeviceInfo(info);
        DeviceProbeStatusText = $"已识别 {info.Model}";
        SetStatus("CAN 设备检测完成", DiagnosticStatusKind.Success);
        AppendLog($"检测到 CAN 设备：{DetectedDeviceText}，{CanFdCapabilityText}");
    }

    private void ApplyDetectedDeviceInfo(CanDeviceInfo? info)
    {
        if (info is null)
        {
            return;
        }

        DetectedCanDeviceInfo = info;
        ResetCanChannels(info.ChannelCount);
    }

    private void ResetCanChannels(uint channelCount)
    {
        var count = Math.Clamp((int)channelCount, 1, 16);
        var selected = int.TryParse(SelectedCanChannel, out var parsed) ? parsed : 0;
        CanChannels.Clear();
        for (var channel = 0; channel < count; channel++)
        {
            CanChannels.Add(channel.ToString());
        }

        SelectedCanChannel = Math.Clamp(selected, 0, count - 1).ToString();
    }

    private async Task OpenCanDeviceAsync(string baudRate, CancellationToken cancellationToken)
    {
        var device = _canDeviceFactory.Create(SelectedDeviceType);
        device.FrameReceived += OnFrameReceived;
        device.FrameSent += OnFrameSent;
        try
        {
            await device.OpenAsync(new CanDeviceOptions
            {
                DeviceType = DetectedCanDeviceInfo is { DeviceType: > 0 }
                    ? DetectedCanDeviceInfo.DeviceType.ToString()
                    : SelectedDeviceType,
                DeviceIndex = DetectedCanDeviceInfo?.DeviceIndex ?? 0,
                BaudRate = HexUtil.ParseBaudRate(baudRate),
                Channel = uint.TryParse(SelectedCanChannel, out var channel) ? channel : 0,
                Protocol = CanProtocolExtensions.Parse(ActiveFlashConfiguration?.CanProtocol),
                CanFdDataBaudRate = ParseFlexibleBaudRate(SelectedCanFdDataBaudRate),
                UseBitRateSwitch = UseBitRateSwitch
            }, cancellationToken);
            _canDevice = device;
        }
        catch
        {
            device.FrameReceived -= OnFrameReceived;
            device.FrameSent -= OnFrameSent;
            await device.DisposeAsync();
            throw;
        }
    }

    private async Task ReleaseCanDeviceAsync(CancellationToken cancellationToken)
    {
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
            await device.CloseAsync(cancellationToken);
        }
        finally
        {
            await device.DisposeAsync();
        }
    }

    private IReadOnlyList<string> ResolveBaudRateCandidates(BaudRateSelectionMode mode)
    {
        if (mode == BaudRateSelectionMode.Project)
        {
            if (ActiveFlashConfiguration is null)
            {
                throw new InvalidOperationException("跟随项目波特率时必须先选择项目。");
            }

            return [ActiveFlashConfiguration.BaudRate];
        }

        if (mode == BaudRateSelectionMode.Fixed)
        {
            return [SelectedBaudRate];
        }

        return new[] { ActiveFlashConfiguration?.BaudRate, SelectedBaudRate }
            .Concat(BaudRates)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<bool> HasBusTrafficAsync(ICanDevice device, CancellationToken cancellationToken)
    {
        var trafficDetected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnTraffic(object? sender, CanFrame frame) => trafficDetected.TrySetResult();
        device.FrameReceived += OnTraffic;
        try
        {
            var delay = Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            var completed = await Task.WhenAny(trafficDetected.Task, delay);
            cancellationToken.ThrowIfCancellationRequested();
            return completed == trafficDetected.Task;
        }
        finally
        {
            device.FrameReceived -= OnTraffic;
        }
    }

    private BaudRateSelectionMode GetBaudRateSelectionMode() => SelectedBaudRateMode switch
    {
        FixedBaudRateModeText => BaudRateSelectionMode.Fixed,
        ProjectBaudRateModeText => BaudRateSelectionMode.Project,
        _ => BaudRateSelectionMode.Auto
    };

    private static string ToBaudRateModeText(BaudRateSelectionMode mode) => mode switch
    {
        BaudRateSelectionMode.Fixed => FixedBaudRateModeText,
        BaudRateSelectionMode.Project => ProjectBaudRateModeText,
        _ => AutoBaudRateModeText
    };

    private static string NormalizeDeviceType(string? deviceType) =>
        string.Equals(deviceType?.Trim(), "ZLG USBCAN-2A (4)", StringComparison.OrdinalIgnoreCase)
            ? "ZLGCAN"
            : string.IsNullOrWhiteSpace(deviceType) ? "Mock" : deviceType.Trim();

    private static uint ParseFlexibleBaudRate(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.EndsWith('M'))
        {
            return uint.Parse(normalized[..^1], System.Globalization.CultureInfo.InvariantCulture) * 1_000_000;
        }

        return HexUtil.ParseBaudRate(normalized);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            if (DetectedCanDeviceInfo is null)
            {
                await ProbeCanDeviceCoreAsync(cancellationToken);
            }

            var mode = GetBaudRateSelectionMode();
            var candidates = ResolveBaudRateCandidates(mode);
            Exception? lastError = null;
            foreach (var baudRate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await OpenCanDeviceAsync(baudRate, cancellationToken);
                    if (mode != BaudRateSelectionMode.Auto
                        || string.Equals(SelectedDeviceType, "Mock", StringComparison.OrdinalIgnoreCase)
                        || await HasBusTrafficAsync(_canDevice!, cancellationToken))
                    {
                        SelectedBaudRate = baudRate;
                        break;
                    }

                    await ReleaseCanDeviceAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await ReleaseCanDeviceAsync(CancellationToken.None);
                }
            }

            if (_canDevice is null)
            {
                throw lastError ?? new InvalidOperationException(
                    mode == BaudRateSelectionMode.Auto
                        ? "自动波特率搜索期间未监听到总线报文，请确认总线有通信，或改用指定/跟随项目模式。"
                        : "没有可用的 CAN 波特率。");
            }

            IsConnected = true;
            ApplyDetectedDeviceInfo(_canDevice.DeviceInfo);
            SetStatus("CAN 已连接", DiagnosticStatusKind.Success);
            var requestedProtocol = CanProtocolExtensions.Parse(ActiveFlashConfiguration?.CanProtocol);
            if (requestedProtocol == CanProtocol.CanFd && _canDevice.ActiveProtocol == CanProtocol.Can)
            {
                AppendLog("当前适配器不支持 CAN FD，连接已回退为经典 CAN。");
            }

            AppendLog($"CAN 已连接：{ConnectedDeviceText}，{SelectedBaudRate}，{_canDevice.ActiveProtocol.ToConfigValue()}，通道 {SelectedCanChannel}");
        }
        catch (Exception ex)
        {
            await ReleaseCanDeviceAsync(CancellationToken.None);
            SetStatus("连接失败", DiagnosticStatusKind.Error);
            AppendLog($"连接失败：{ex.Message}");
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
            await ReleaseCanDeviceAsync(cancellationToken);

            IsConnected = false;
            SetStatus("CAN 已断开", DiagnosticStatusKind.Neutral);
            AppendLog("CAN 已断开。");
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
        ChooseApplicationDataDirectoryCommand.Cancel();

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

    private void ResetFlashProgress()
    {
        Progress = 0;
        DownloadStatusKind = DiagnosticStatusKind.Neutral;
        if (_flashStartedAt is null)
        {
            return;
        }

        _flashStartedAt = null;
        _flashStopwatch.Reset();
        OnPropertyChanged(nameof(FlashStartTimeText));
        OnPropertyChanged(nameof(FlashDurationText));
    }

    private async Task StartFlashAsync(CancellationToken cancellationToken)
    {
        var flashConfiguration = ActiveFlashConfiguration;
        var logProjectName = SelectedProject?.ProjectName;
        if (_canDevice is null || flashConfiguration is null)
        {
            return;
        }

        var durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        durationTimer.Tick += (_, _) => OnPropertyChanged(nameof(FlashDurationText));

        try
        {
            IsBusy = true;
            Progress = 0;
            _flashStartedAt = DateTimeOffset.Now;
            _flashStopwatch.Restart();
            OnPropertyChanged(nameof(FlashStartTimeText));
            OnPropertyChanged(nameof(FlashDurationText));
            durationTimer.Start();
            DownloadStatusKind = DiagnosticStatusKind.Running;
            SetStatus("Flashing", DiagnosticStatusKind.Running);

            var bootFile = SelectedBootConfig ?? flashConfiguration.BootConfigFile;
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

            var firmwareSet = LoadFirmwareSet(flashConfiguration, bootConfig);
            var projectProtocol = CanProtocolExtensions.Parse(flashConfiguration.CanProtocol);
            if (projectProtocol == CanProtocol.Can && _canDevice.ActiveProtocol == CanProtocol.CanFd)
            {
                throw new InvalidOperationException(
                    $"当前连接协议为 {_canDevice.ActiveProtocol.ToConfigValue()}，项目要求 {projectProtocol.ToConfigValue()}，请断开后重新连接。");
            }
            if (projectProtocol == CanProtocol.CanFd
                && _canDevice.ActiveProtocol == CanProtocol.Can
                && _canDevice.DeviceInfo?.SupportsCanFd == true)
            {
                throw new InvalidOperationException("当前设备支持 CAN FD，但本次连接使用经典 CAN，请断开后按 CAN FD 项目重新连接。");
            }

            var options = new DiagnosticTransportOptions
            {
                PhysicalRequestId = HexUtil.ParseUInt32(flashConfiguration.PhysicalRequestId),
                FunctionalRequestId = HexUtil.ParseUInt32(flashConfiguration.FunctionalRequestId),
                ResponseId = HexUtil.ParseUInt32(flashConfiguration.ResponseAddressId),
                ExtendedFrame = HexUtil.ParseUInt32(flashConfiguration.PhysicalRequestId) > 0x7FF
                    || HexUtil.ParseUInt32(flashConfiguration.FunctionalRequestId) > 0x7FF
                    || HexUtil.ParseUInt32(flashConfiguration.ResponseAddressId) > 0x7FF,
                Channel = uint.TryParse(SelectedCanChannel, out var selectedChannel)
                    ? selectedChannel
                    : 0,
                FrameDataLength = _canDevice.ActiveProtocol == CanProtocol.CanFd ? 64 : 8
            };

            if (_canDevice is MockCanDevice mockDevice)
            {
                mockDevice.ConfigureTransport(options);
            }

            using var transport = new IsoTpTransport(_canDevice, options);
            var udsClient = new UdsClient(transport);
            IFlashFlowExecutor executor = new FlashFlowExecutor(udsClient, _seedKeyAlgorithmRegistry);
            var progress = new Progress<FlashProgress>(item =>
            {
                Progress = Math.Clamp(item.Percent, 0, 100);
                DownloadStatusKind = DiagnosticStatusKind.Running;
                CurrentExecutingStepId = item.StepId;
                SetStatus(item.Message, DiagnosticStatusKind.Running);
            });

            var result = await executor.ExecuteAsync(
                new FlashExecutionRequest(
                    bootConfig,
                    firmwareSet)
                {
                    LogProgress = new Progress<string>(message => AppendLog(message, logProjectName))
                },
                progress,
                cancellationToken);

            CurrentExecutingStepId = null;
            DownloadStatusKind = result.Success ? DiagnosticStatusKind.Success : DiagnosticStatusKind.Error;
            var resultMessage = result.Success
                ? "刷写完成"
                : string.IsNullOrWhiteSpace(result.UserMessage) ? "刷写失败" : result.UserMessage;
            SetStatus(resultMessage, DownloadStatusKind);
            Progress = result.Success ? 100 : Progress;
        }
        catch (OperationCanceledException)
        {
            CurrentExecutingStepId = null;
            DownloadStatusKind = DiagnosticStatusKind.Warning;
            SetStatus("刷写已取消", DiagnosticStatusKind.Warning);
            AppendLog("刷写已取消。");
        }
        catch (Exception ex)
        {
            CurrentExecutingStepId = null;
            DownloadStatusKind = DiagnosticStatusKind.Error;
            SetStatus(ex.Message, DiagnosticStatusKind.Error);
            AppendLog($"刷写失败：{ex.Message}");
        }
        finally
        {
            durationTimer.Stop();
            _flashStopwatch.Stop();
            OnPropertyChanged(nameof(FlashDurationText));
            IsBusy = false;
        }
    }

    private async Task SendManualFrameAsync(CancellationToken cancellationToken)
    {
        var device = _canDevice;
        if (device is null)
        {
            return;
        }

        try
        {
            var data = HexUtil.ParseBytes(ManualFrameData);
            var isCanFd = device.ActiveProtocol == CanProtocol.CanFd;
            var maximumLength = isCanFd ? 64 : 8;
            if (data.Length > maximumLength)
            {
                throw new InvalidOperationException($"当前协议单帧数据不能超过 {maximumLength} 字节。");
            }

            var channel = uint.TryParse(SelectedCanChannel, out var parsedChannel) ? parsedChannel : 0;
            var frameId = ParseManualFrameId(ManualFrameId);
            var isExtended = _isManualFrameExtended ?? frameId > MaxStandardCanId;
            if (frameId > (isExtended ? MaxExtendedCanId : MaxStandardCanId))
            {
                throw new InvalidOperationException(isExtended
                    ? "扩展帧 ID 不能超过 0x1FFFFFFF。"
                    : "标准帧 ID 不能超过 0x7FF，请修改 ID 或选择扩展帧。");
            }

            var frame = new CanFrame(
                (uint)frameId,
                data,
                channel,
                isExtended,
                false,
                IsCanFd: isCanFd,
                BitRateSwitch: isCanFd && UseBitRateSwitch);
            await device.SendAsync(frame, cancellationToken);
            SetStatus("报文已发送", DiagnosticStatusKind.Success);
            AppendLog($"Manual TX: {frame}");
        }
        catch (Exception ex)
        {
            SetStatus($"发送报文失败：{ex.Message}", DiagnosticStatusKind.Error);
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
            RequiresDownloadStep(bootConfig, FlashStepKind.DownloadDriver));

        var application = LoadFirmwareImage(
            applicationPath,
            FirmwareImageKind.Application,
            "应用",
            RequiresDownloadStep(bootConfig, FlashStepKind.DownloadApplication));

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
        bool requiresDownload)
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

        var image = _firmwareLoader.Load(path, kind);
        AppendLog($"{label} image loaded: {Path.GetFileName(path)}, {image.Length} bytes");
        return image;
    }

    private void OpenNewProjectEditor()
    {
        if (!NavigateToUdsPage(3))
        {
            return;
        }
        ProjectEditor.BeginNew(BootConfigFiles.FirstOrDefault() ?? string.Empty);
        UpdateProjectEditorFirmwareRequirements();
        SetProjectEditorMode(null, creatingProject: true);
    }

    private void EditSelectedProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        ProjectEditor.Load(SelectedProject);
        UpdateProjectEditorFirmwareRequirements();
        SetProjectEditorMode(SelectedProject);
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
        RefreshProjectFileStatus(project);

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
            var originalProjectName = _editingProject!.ProjectName;
            if (!string.Equals(originalProjectName, project.ProjectName, StringComparison.Ordinal))
            {
                for (var index = 0; index < LogEntries.Count; index++)
                {
                    var entry = LogEntries[index];
                    if (string.Equals(entry.ProjectName, originalProjectName, StringComparison.OrdinalIgnoreCase))
                    {
                        LogEntries[index] = entry with { ProjectName = project.ProjectName };
                    }
                }
            }
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
                $"删除后将移除项目“{projectName}”的全部项目信息，且无法恢复。是否确认删除？",
                "确认删除项目",
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
        AppendLog($"项目已删除: {projectName}", projectName);
        CloseProjectEditor();
    }

    private void CloseProjectEditor() => SetProjectEditorMode(null);

    private void SetProjectEditorMode(ProjectConfigEntry? editingProject, bool creatingProject = false)
    {
        if (ReferenceEquals(_editingProject, editingProject) && _isNewProjectDialogVisible == creatingProject)
        {
            return;
        }

        _editingProject = editingProject;
        SetProperty(ref _isNewProjectDialogVisible, creatingProject, nameof(IsNewProjectDialogVisible));
        OnPropertyChanged(nameof(IsEditingProject));
        OnPropertyChanged(nameof(IsProjectEditorVisible));
        OnPropertyChanged(nameof(CanDeleteEditedProject));
        RaiseProjectEditorCommandStates();
    }

    private void AddProject()
    {
        var project = new ProjectConfigEntry
        {
            ProjectName = CreateDefaultProjectName(),
            CanProtocol = "CAN",
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

    private string CreateDefaultProjectName()
    {
        var index = Projects.Count + 1;
        while (Projects.Any(project => string.Equals(
                   project.ProjectName,
                   $"New Project {index}",
                   StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"New Project {index}";
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

            if (!string.Equals(project.CanProtocol, "CAN", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(project.CanProtocol, "CANFD", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"项目 {project.ProjectName}：CAN 协议必须为 CAN 或 CANFD。");
            }

            if (string.IsNullOrWhiteSpace(project.BootConfigFile))
            {
                issues.Add($"Project {project.ProjectName}: boot config is empty.");
            }
        }

        issues.AddRange(projectList.GroupBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => $"Duplicate project name: {group.Key}"));
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

        if (ActiveFlashConfiguration is null)
        {
            UpdateProjectFlowPreview(null, "请在侧栏选择刷写项目。");
            SetFlowValidationNotice("未选择项目。");
            RefreshFlowAlgorithmOptions();
            MarkFlowAsBaseline();
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedBootConfig))
        {
            UpdateProjectFlowPreview(null, "此项目未关联 BOOT 配置，请联系管理员配置。");
            SetFlowValidationNotice("请选择 BOOT 配置文件。");
            RefreshFlowAlgorithmOptions();
            MarkFlowAsBaseline();
            return;
        }

        try
        {
            _loadedFlowConfig = IsProjectFlowDraft
                ? _draftFlowConfig ?? throw new InvalidOperationException("配置草稿不存在。")
                : _bootConfigRepository.Load(SelectedBootConfig);

            PopulateFlowRows(_loadedFlowConfig);
            UpdateProjectFlowPreview(_loadedFlowConfig);
            var source = IsProjectFlowDraft ? "draft" : SelectedBootConfig;
            var displaySource = IsProjectFlowDraft ? "草稿" : source;
            AppendLog($"Flow loaded: {source}, steps={FlowRows.Count}",
                SelectedProject?.ProjectName, $"已加载流程 {displaySource}（{FlowRows.Count} 步）");
        }
        catch (Exception ex)
        {
            UpdateProjectFlowPreview(null, ex is FileNotFoundException
                ? "找不到此项目的 BOOT 配置文件，请联系管理员检查文件路径。"
                : "无法读取此项目的 BOOT 流程，请联系管理员检查配置文件。");
            SetFlowValidationNotice(ex.Message);
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
            var isDraft = IsProjectFlowDraft;
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
            UpdateProjectFlowPreview(config);
            RefreshProjectFileStatuses();
            UpdateProjectEditorFirmwareRequirements();
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

    private void UpdateProjectFlowPreview(BootConfig? config, string emptyNotice = "当前 BOOT 配置没有流程节点。")
    {
        ProjectFlowSteps = config?.Flow.Select(ProjectFlowStep.FromConfig).ToArray() ?? [];
        ProjectFlowNotice = ProjectFlowSteps.Count == 0 ? emptyNotice : string.Empty;
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
        if (ActiveFlashConfiguration is null)
        {
            return false;
        }

        if (IsProjectFlowDraft)
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

        var configName = ActiveFlashConfiguration?.ProjectName ?? SelectedBootConfig ?? "当前配置";
        var dialog = new UnsavedChangesDialog(configName, action);
        _ = dialog.ShowDialog();
        if (dialog.Choice == UnsavedChangesChoice.Cancel)
        {
            return false;
        }

        if (dialog.Choice == UnsavedChangesChoice.Save)
        {
            return TrySaveFlowConfig();
        }

        if (IsProjectFlowDraft)
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
            project.RefreshFirmwareFileStatuses(config, _firmwareLoader);

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
        return new ProjectConfigEntry
        {
            ProjectName = projectName,
            CanProtocol = "CAN",
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

        ApplyFlowValidationResult(issues, warnings);

        // Warnings must not block saving: a declaration the executor does not honour yet
        // is reported, but it does not stop the operator from editing the flow.
        return issues.Count == 0;
    }

    private void SetFlowValidationNotice(string message)
    {
        _hasFlowValidationResult = false;
        FlowValidationLines.Clear();
        FlowValidationLines.Add(new FlowValidationLine(FlowValidationSeverity.Info, message));
        FlowValidationText = message;
        RaiseFlowValidationChanged();
    }

    private void ApplyFlowValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        _hasFlowValidationResult = true;
        FlowValidationLines.Clear();
        foreach (var error in errors)
        {
            FlowValidationLines.Add(new FlowValidationLine(FlowValidationSeverity.Error, error));
        }

        foreach (var warning in warnings)
        {
            FlowValidationLines.Add(new FlowValidationLine(FlowValidationSeverity.Warning, warning));
        }

        FlowValidationText = errors.Count == 0 && warnings.Count == 0
            ? $"校验通过：共 {FlowRows.Count} 个流程节点。"
            : string.Join(
                Environment.NewLine,
                errors.Select(text => $"错误：{text}").Concat(warnings.Select(text => $"提示：{text}")));
        RaiseFlowValidationChanged();
    }

    private void RaiseFlowValidationChanged()
    {
        OnPropertyChanged(nameof(HasFlowValidationResult));
        OnPropertyChanged(nameof(FlowValidationErrorCount));
        OnPropertyChanged(nameof(FlowValidationWarningCount));
        OnPropertyChanged(nameof(FlowValidationStateKind));
        OnPropertyChanged(nameof(FlowValidationStatusText));
        OnPropertyChanged(nameof(FlowValidationSummaryText));
    }

    private void UpdateExecutingFlowRow()
    {
        foreach (var row in FlowRows)
        {
            row.IsExecuting = _currentExecutingStepId is { } stepId && row.Id == stepId;
        }
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
        if (!CanEditFlow)
        {
            return;
        }

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
        var project = SelectedProject;
        var bootFile = project?.BootConfigFile;
        SelectedBootConfig = string.IsNullOrWhiteSpace(bootFile) ? null : bootFile.Trim();
        if (project is not null && GetBaudRateSelectionMode() == BaudRateSelectionMode.Project)
        {
            SelectedBaudRate = project.BaudRate;
        }
        DriverFilePath = project?.DriveFilePath ?? string.Empty;
        ApplicationFilePath = project?.FlashFilePath ?? string.Empty;
        LoadFlowFromSelected();
        OnPropertyChanged(nameof(RequestedProtocolText));
        OnPropertyChanged(nameof(ProtocolStatusText));
    }

    private static void NormalizeProjectDefaults(ProjectConfigEntry project)
    {
        project.CanProtocol = CanProtocolExtensions.Parse(project.CanProtocol).ToConfigValue();
    }

    private void OnProjectEditorPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ProjectEditorViewModel.BootConfigFile))
        {
            UpdateProjectEditorFirmwareRequirements();
        }
    }

    private void UpdateProjectEditorFirmwareRequirements()
    {
        if (string.IsNullOrWhiteSpace(ProjectEditor.BootConfigFile))
        {
            ProjectEditor.SetFirmwareRequirements(false, false);
            return;
        }

        try
        {
            var bootConfig = _bootConfigRepository.Load(ProjectEditor.BootConfigFile);
            ProjectEditor.SetFirmwareRequirements(
                RequiresDownloadStep(bootConfig, FlashStepKind.DownloadDriver),
                RequiresDownloadStep(bootConfig, FlashStepKind.DownloadApplication));
        }
        catch
        {
            // 配置内容损坏时无法可靠判断下载步骤，保留两个文件入口以避免保存时误清已有路径。
            ProjectEditor.SetFirmwareRequirements(true, true);
        }
    }

    private void RefreshProjectFileStatuses()
    {
        var bootConfigCache = new Dictionary<string, (BootConfig? Config, string? Error)>(
            StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < Projects.Count; index++)
        {
            var project = Projects[index];
            if (ReferenceEquals(project, _draftFlowProject) && _draftFlowConfig is not null)
            {
                project.RefreshFirmwareFileStatuses(_draftFlowConfig, _firmwareLoader);
                continue;
            }

            var configReference = project.BootConfigFile?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configReference))
            {
                project.RefreshFirmwareFileStatuses(
                    null,
                    _firmwareLoader,
                    "项目未配置 BOOT 配置文件。");
                continue;
            }

            if (!bootConfigCache.TryGetValue(configReference, out var result))
            {
                try
                {
                    result = (_bootConfigRepository.Load(configReference), null);
                }
                catch (Exception ex)
                {
                    result = (null, ex.Message);
                }

                bootConfigCache[configReference] = result;
            }

            project.RefreshFirmwareFileStatuses(result.Config, _firmwareLoader, result.Error);
        }
    }

    private void RefreshProjectFileStatus(ProjectConfigEntry project)
    {
        if (string.IsNullOrWhiteSpace(project.BootConfigFile))
        {
            project.RefreshFirmwareFileStatuses(null, _firmwareLoader, "项目未配置 BOOT 配置文件。");
            return;
        }

        try
        {
            project.RefreshFirmwareFileStatuses(
                _bootConfigRepository.Load(project.BootConfigFile),
                _firmwareLoader);
        }
        catch (Exception ex)
        {
            project.RefreshFirmwareFileStatuses(null, _firmwareLoader, ex.Message);
        }
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
            ? "CAN ID 过滤已开启，仅显示当前配置相关 ID"
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
            var lines = new[] { "Time,Direction,Channel,ID,Format,Protocol,BRS,ESI,DLC,Data" }
                .Concat(exportFrames.Select(frame => string.Join(
                    ",",
                    frame.Timestamp,
                    frame.Direction,
                    frame.Channel,
                    frame.IdText,
                    frame.FormatText,
                    frame.ProtocolText,
                    frame.BrsText,
                    frame.EsiText,
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
        var text = GetManualFrameIdDigits(value);
        if (text.IsEmpty)
        {
            throw new FormatException("CAN ID 不能为空。");
        }

        try
        {
            return ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("ID超出有效范围");
        }
    }

    private static ReadOnlySpan<char> GetManualFrameIdDigits(string value)
    {
        var text = value.AsSpan().Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
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

        if (ActiveFlashConfiguration is null)
        {
            return true;
        }

        var projectIds = new[]
        {
            ActiveFlashConfiguration.PhysicalRequestId,
            ActiveFlashConfiguration.FunctionalRequestId,
            ActiveFlashConfiguration.ResponseAddressId
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
        AppendLog(message, SelectedProject?.ProjectName);
    }

    private void AppendLog(string message, string? projectName, string? displayMessage = null)
    {
        var entry = new SystemLogEntry(DateTime.Now, ClassifyStatusText(message), message)
        {
            ProjectName = projectName,
            DisplayMessage = displayMessage ?? message
        };
        RunOnUi(() =>
        {
            LogEntries.Add(entry);
            LatestLogEntry = entry;

            while (LogEntries.Count > MaxLogEntries)
            {
                LogEntries.RemoveAt(0);
            }

            ApplyLogFilters();
            RaiseLogSummaryProperties();
        });
        WriteLogLine(entry.Text);
    }

    private void ApplyLogFilters()
    {
        WorkspaceLogEntries.Clear();
        foreach (var entry in LogEntries.Where(LogEntryMatchesWorkspace))
        {
            WorkspaceLogEntries.Add(entry);
        }
        UpdateDownloadInfoEntries();
        LatestLogEntry = WorkspaceLogEntries.LastOrDefault();
        FilteredLogEntries.Clear();
        foreach (var entry in WorkspaceLogEntries.Where(LogEntryMatchesFilter))
        {
            FilteredLogEntries.Add(entry);
        }

        RaiseLogSummaryProperties();
    }

    private void UpdateDownloadInfoEntries()
    {
        var start = Math.Max(0, WorkspaceLogEntries.Count - MaxDownloadInfoEntries);
        var count = WorkspaceLogEntries.Count - start;

        // Keep rows already in the retained window so a new log does not clear their selection.
        while (DownloadInfoEntries.Count > 0
               && (count == 0 || !ReferenceEquals(DownloadInfoEntries[0], WorkspaceLogEntries[start])))
        {
            DownloadInfoEntries.RemoveAt(0);
        }

        var retained = 0;
        while (retained < count && retained < DownloadInfoEntries.Count
               && ReferenceEquals(DownloadInfoEntries[retained], WorkspaceLogEntries[start + retained]))
        {
            retained++;
        }

        while (DownloadInfoEntries.Count > retained)
        {
            DownloadInfoEntries.RemoveAt(DownloadInfoEntries.Count - 1);
        }

        for (var index = retained; index < count; index++)
        {
            DownloadInfoEntries.Add(WorkspaceLogEntries[start + index]);
        }

        OnPropertyChanged(nameof(ShowDownloadInfoSources));
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

    private string GetPathStatusDisplay(string path, DiagnosticStatusKind statusKind)
        => ApplyPathStatus(GetCompactPathDisplay(path), statusKind);

    private static string ApplyPathStatus(string displayPath, DiagnosticStatusKind statusKind)
        => statusKind == DiagnosticStatusKind.Warning
            ? $"路径缺失 · {displayPath}"
            : displayPath;

    private static bool IsLogFileLocationAvailable(string path)
    {
        if (File.Exists(path))
        {
            return true;
        }

        var directory = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
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

    private static void CopyFileIfMissing(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(destinationPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath);
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

    private static void CopyDirectoryMissing(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            CopyFileIfMissing(sourceFile, Path.Combine(destinationDirectory, relativePath));
        }
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        RefreshProjectsCommand.RaiseCanExecuteChanged();
        BrowseDriverCommand.RaiseCanExecuteChanged();
        BrowseApplicationCommand.RaiseCanExecuteChanged();
        OpenActiveProjectConfigCommand.RaiseCanExecuteChanged();
        OpenLogFileCommand.RaiseCanExecuteChanged();
        OpenLogDirectoryCommand.RaiseCanExecuteChanged();
        NavigateToSystemLogCommand.RaiseCanExecuteChanged();
        ClearFramesCommand.RaiseCanExecuteChanged();
        ToggleFrameCaptureCommand.RaiseCanExecuteChanged();
        ToggleFrameFilterCommand.RaiseCanExecuteChanged();
        ExportFramesCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ToggleConnectionCommand.RaiseCanExecuteChanged();
        ProbeCanDeviceCommand.RaiseCanExecuteChanged();
        StartFlashCommand.RaiseCanExecuteChanged();
        CancelFlashCommand.RaiseCanExecuteChanged();
        DeleteProjectCommand.RaiseCanExecuteChanged();
        SaveProjectsCommand.RaiseCanExecuteChanged();
        AddProjectCommand.RaiseCanExecuteChanged();
        RaiseProjectEditorCommandStates();
        LoadFlowCommand.RaiseCanExecuteChanged();
        SaveFlowCommand.RaiseCanExecuteChanged();
        ValidateFlowCommand.RaiseCanExecuteChanged();
        NewFlowCommand.RaiseCanExecuteChanged();
        RaiseAddFlowStepCommandStates();
        ReplaceFlowStepTemplateCommand.RaiseCanExecuteChanged();
        DeleteFlowStepCommand.RaiseCanExecuteChanged();
        MoveFlowStepUpCommand.RaiseCanExecuteChanged();
        MoveFlowStepDownCommand.RaiseCanExecuteChanged();
        SendManualFrameCommand.RaiseCanExecuteChanged();
        ChooseApplicationDataDirectoryCommand.RaiseCanExecuteChanged();
        ChooseFlowConfigDirectoryCommand.RaiseCanExecuteChanged();
        ChooseProjectConfigFileCommand.RaiseCanExecuteChanged();
        ImportBuiltinConfigurationCommand.RaiseCanExecuteChanged();
        ClearRuntimeConfigurationCommand.RaiseCanExecuteChanged();
    }

    private void RaiseAddFlowStepCommandStates()
    {
        ConfirmAddFlowStepCommand.RaiseCanExecuteChanged();
        CancelAddFlowStepCommand.RaiseCanExecuteChanged();
    }

    private void RaiseProjectEditorCommandStates()
    {
        NewProjectCommand.RaiseCanExecuteChanged();
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
        RaiseLogListStateProperties();
    }

    private void RaiseLogListStateProperties()
    {
        OnPropertyChanged(nameof(LogFilterSummaryText));
        OnPropertyChanged(nameof(IsLogListEmpty));
        OnPropertyChanged(nameof(LogEmptyStateText));
        OnPropertyChanged(nameof(LogEmptyStateHintText));
    }

    private void RaiseDeviceStatusProperties()
    {
        OnPropertyChanged(nameof(DeviceStatusText));
        OnPropertyChanged(nameof(DeviceStatusKind));
        OnPropertyChanged(nameof(DeviceStatusIcon));
        OnPropertyChanged(nameof(ConnectionSummaryText));
    }

    private void RaiseConnectionDetailProperties()
    {
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(BaudRateStatusText));
        OnPropertyChanged(nameof(ConnectionSummaryText));
        OnPropertyChanged(nameof(ConnectedDeviceText));
        OnPropertyChanged(nameof(RequestedProtocolText));
        OnPropertyChanged(nameof(ActiveProtocolText));
        OnPropertyChanged(nameof(ProtocolStatusText));
        OnPropertyChanged(nameof(StatusBarDeviceText));
    }

    private sealed class AppSettingsSnapshot
    {
        [JsonPropertyName("UserRole")]
        public string? UserRole { get; set; }
        public bool KeepRunningInTray { get; set; }
        public bool? AutoSearchBaudRate { get; set; }
        public string? BaudRateSelectionMode { get; set; }
        public bool LogFileEnabled { get; set; } = true;
        public string? LogFilePath { get; set; }
        public int? LogRetentionDays { get; set; }
        public int? LogMaxFileSizeMb { get; set; }
        public bool LogAutoScrollEnabled { get; set; } = true;
        public string? SelectedDeviceType { get; set; }
        public string? SelectedBaudRate { get; set; }
        public string? SelectedCanChannel { get; set; }
        public string? CanFdDataBaudRate { get; set; }
        public bool? UseBitRateSwitch { get; set; }
        public string? ProjectConfigPath { get; set; }
        public string? FlowConfigDirectory { get; set; }
    }
}
