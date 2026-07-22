using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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
    private readonly AppConfigurationPaths _paths = new();
    private readonly JsonProjectConfigRepository _projectRepository;
    private readonly JsonBootConfigRepository _bootConfigRepository;
    private readonly FirmwareLoader _firmwareLoader = new();
    private readonly CanDeviceFactory _canDeviceFactory = new();
    private ICanDevice? _canDevice;
    private BootConfig? _loadedFlowConfig;
    private ProjectConfigEntry? _selectedProject;
    private FlowStepEditorRow? _selectedFlowStep;
    private string? _selectedBootConfig;
    private string _selectedDeviceType = "Mock";
    private string _selectedBaudRate = "500K";
    private string _driverFilePath = string.Empty;
    private string _applicationFilePath = string.Empty;
    private string _manualFrameId = "0x18DA5535";
    private string _manualFrameData = "02 10 03";
    private string _manualChannel = "0";
    private string _selectedFunctionCheckConfig = "mock";
    private bool _manualIsExtended = true;
    private bool _isConnected;
    private bool _isBusy;
    private int _selectedShellIndex;
    private int _progress;
    private string _statusText = "Ready";
    private DiagnosticStatusKind _statusKind = DiagnosticStatusKind.Neutral;
    private DiagnosticStatusKind _downloadStatusKind = DiagnosticStatusKind.Neutral;
    private string _flowValidationText = "No flow loaded.";

    public MainViewModel()
    {
        _projectRepository = new JsonProjectConfigRepository(_paths);
        _bootConfigRepository = new JsonBootConfigRepository(_paths);

        RefreshCommand = new RelayCommand(Refresh);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected && !IsBusy);
        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => !IsBusy);
        BrowseDriverCommand = new RelayCommand(() => BrowseFirmware(path => DriverFilePath = path));
        BrowseApplicationCommand = new RelayCommand(() => BrowseFirmware(path => ApplicationFilePath = path));
        StartFlashCommand = new AsyncRelayCommand(StartFlashAsync, () => IsConnected && SelectedProject is not null && !IsBusy);
        ClearLogCommand = new RelayCommand(() => LogLines.Clear());
        ClearFramesCommand = new RelayCommand(() => Frames.Clear());
        AddProjectCommand = new RelayCommand(AddProject);
        DeleteProjectCommand = new RelayCommand(DeleteSelectedProject, () => SelectedProject is not null);
        SaveProjectsCommand = new RelayCommand(SaveProjects);
        LoadFlowCommand = new RelayCommand(LoadFlowFromSelected, () => !string.IsNullOrWhiteSpace(SelectedBootConfig));
        SaveFlowCommand = new RelayCommand(SaveFlowConfig, () => !string.IsNullOrWhiteSpace(SelectedBootConfig) && FlowRows.Count > 0);
        ValidateFlowCommand = new RelayCommand(() => ValidateFlowConfig());
        AddFlowStepCommand = new RelayCommand(AddFlowStep);
        DeleteFlowStepCommand = new RelayCommand(DeleteSelectedFlowStep, () => SelectedFlowStep is not null);
        MoveFlowStepUpCommand = new RelayCommand(() => MoveSelectedFlowStep(-1), () => SelectedFlowStep is not null);
        MoveFlowStepDownCommand = new RelayCommand(() => MoveSelectedFlowStep(1), () => SelectedFlowStep is not null);
        SendManualFrameCommand = new AsyncRelayCommand(SendManualFrameAsync, () => IsConnected && !IsBusy);
        StartFunctionCheckCommand = new AsyncRelayCommand(StartFunctionCheckAsync, () => !IsBusy);

        LoadDefaultFunctionChecks();
        Refresh();
    }

    public ObservableCollection<ProjectConfigEntry> Projects { get; } = [];
    public ObservableCollection<string> BootConfigFiles { get; } = [];
    public ObservableCollection<string> DeviceTypes { get; } = ["Mock", "ZLG USBCAN-2A (4)"];
    public ObservableCollection<string> BaudRates { get; } = ["250K", "500K", "1000K"];
    public ObservableCollection<string> FunctionCheckConfigs { get; } = ["mock"];
    public ObservableCollection<string> LogLines { get; } = [];
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
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand ClearFramesCommand { get; }
    public RelayCommand AddProjectCommand { get; }
    public RelayCommand DeleteProjectCommand { get; }
    public RelayCommand SaveProjectsCommand { get; }
    public RelayCommand LoadFlowCommand { get; }
    public RelayCommand SaveFlowCommand { get; }
    public RelayCommand ValidateFlowCommand { get; }
    public RelayCommand AddFlowStepCommand { get; }
    public RelayCommand DeleteFlowStepCommand { get; }
    public RelayCommand MoveFlowStepUpCommand { get; }
    public RelayCommand MoveFlowStepDownCommand { get; }
    public AsyncRelayCommand SendManualFrameCommand { get; }
    public AsyncRelayCommand StartFunctionCheckCommand { get; }

    public ProjectConfigEntry? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                ApplySelectedProject();
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
        set => SetProperty(ref _selectedDeviceType, value);
    }

    public string SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => SetProperty(ref _selectedBaudRate, value);
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

    public string ManualChannel
    {
        get => _manualChannel;
        set => SetProperty(ref _manualChannel, value);
    }

    public bool ManualIsExtended
    {
        get => _manualIsExtended;
        set => SetProperty(ref _manualIsExtended, value);
    }

    public string SelectedFunctionCheckConfig
    {
        get => _selectedFunctionCheckConfig;
        set => SetProperty(ref _selectedFunctionCheckConfig, value);
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
            }
        }
    }

    public string FlowValidationText
    {
        get => _flowValidationText;
        set => SetProperty(ref _flowValidationText, value);
    }

    public string ConnectionText => IsConnected ? $"Connected: {SelectedDeviceType} / {SelectedBaudRate}" : "Disconnected";
    public string ConnectionActionText => IsConnected ? "断开设备" : "连接设备";
    public string DeviceStatusText => IsConnected
        ? "已连接"
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

    public string ConfigRootText => _paths.ConfigDirectory;

    private bool IsConnectionFailure => !IsConnected
        && StatusKind == DiagnosticStatusKind.Error
        && ContainsAny(StatusText, "connect failed", "connection failed", "连接失败");

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
        AppendLog($"Configuration loaded: projects={Projects.Count}, boot={BootConfigFiles.Count}");
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
                Channel = 0
            }, cancellationToken);

            IsConnected = true;
            SetStatus("CAN connected", DiagnosticStatusKind.Success);
            AppendLog($"CAN connected: {SelectedDeviceType}, {SelectedBaudRate}");
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

            var channel = uint.TryParse(ManualChannel, out var parsedChannel) ? parsedChannel : 0;
            var frame = new CanFrame(HexUtil.ParseUInt32(ManualFrameId), data, channel, ManualIsExtended);
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
        var validation = ValidateProjects();
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

    private string ValidateProjects()
    {
        var issues = new List<string>();
        foreach (var project in Projects)
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

        issues.AddRange(Projects.GroupBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => $"Duplicate project name: {group.Key}"));
        issues.AddRange(Projects.GroupBy(project => project.ProjectNo)
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
        ManualFrameId = SelectedProject.PhysicalRequestId;
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

    private void OnFrameReceived(object? sender, CanFrame frame) => AddFrame("RX", frame);

    private void OnFrameSent(object? sender, CanFrame frame) => AddFrame("TX", frame);

    private void AddFrame(string direction, CanFrame frame)
    {
        RunOnUi(() =>
        {
            Frames.Add(new CanFrameRow(direction, frame));
            while (Frames.Count > 1000)
            {
                Frames.RemoveAt(0);
            }
        });
    }

    private void AppendLog(string message)
    {
        RunOnUi(() =>
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (LogLines.Count > 1000)
            {
                LogLines.RemoveAt(0);
            }
        });
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

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ToggleConnectionCommand.RaiseCanExecuteChanged();
        StartFlashCommand.RaiseCanExecuteChanged();
        DeleteProjectCommand.RaiseCanExecuteChanged();
        LoadFlowCommand.RaiseCanExecuteChanged();
        SaveFlowCommand.RaiseCanExecuteChanged();
        DeleteFlowStepCommand.RaiseCanExecuteChanged();
        MoveFlowStepUpCommand.RaiseCanExecuteChanged();
        MoveFlowStepDownCommand.RaiseCanExecuteChanged();
        SendManualFrameCommand.RaiseCanExecuteChanged();
        StartFunctionCheckCommand.RaiseCanExecuteChanged();
    }

    private void RaiseDeviceStatusProperties()
    {
        OnPropertyChanged(nameof(DeviceStatusText));
        OnPropertyChanged(nameof(DeviceStatusKind));
        OnPropertyChanged(nameof(DeviceStatusIcon));
    }
}
