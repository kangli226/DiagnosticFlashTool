using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using DiagnosticFlashTool.Application;
using DiagnosticFlashTool.Core.Can;
using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Diagnostics;
using DiagnosticFlashTool.Core.Flashing;
using DiagnosticFlashTool.Core.Util;
using DiagnosticFlashTool.Infrastructure.Security;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;

namespace DiagnosticFlashTool.Product.EcuX.ViewModels;

public sealed class ProductViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly FlashSessionService _session = new();
    private readonly string _profilePath = Path.Combine(AppContext.BaseDirectory, "resources", "product", "ecu-profile.json");
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiagnosticFlashTool",
        "EcuX",
        "settings.json");
    private EcuProductProfile _profile = new();
    private CancellationTokenSource? _flashCts;
    private string _selectedDeviceType = "Mock";
    private string _deviceIndexText = "0";
    private string _channelText = "0";
    private string _baudRateText = "500K";
    private string _driverFilePath = string.Empty;
    private string? _selectedApplicationPath;
    private string _securityDllPath = string.Empty;
    private string _algorithmName = "ECU_DLL";
    private string _entryPoint = "GenerateKeyEx";
    private string _callingConvention = "cdecl";
    private string _securityLevelText = "0x01";
    private string _variant = string.Empty;
    private string _optionsText = string.Empty;
    private string _p2ClientMsText = "5000";
    private string _p2StarClientMsText = "5100";
    private string _s3ClientMsText = "5000";
    private string _pendingOverallTimeoutMsText = "30000";
    private bool _isConnected;
    private bool _isBusy;
    private int _progress;
    private string _currentStep = "尚未开始";
    private string _statusText = "就绪";
    private string _profileSummary = "ECU-X";

    public ProductViewModel()
    {
        DeviceTypes = ["Mock", "ZLG USBCAN-2A (4)"];
        CallingConventions = ["cdecl", "stdcall"];
        ApplicationFiles = [];
        FlashSteps = [];
        LogEntries = [];
        LogEntries.CollectionChanged += LogEntries_CollectionChanged;

        RefreshCommand = new RelayCommand(LoadProfile, () => !IsBusy && !IsConnected);
        BrowseDriverCommand = new RelayCommand(BrowseDriver, () => !IsBusy);
        BrowseApplicationCommand = new RelayCommand(BrowseApplication, () => !IsBusy);
        RemoveApplicationCommand = new RelayCommand(RemoveSelectedApplication, () => SelectedApplicationPath is not null && !IsBusy);
        BrowseSecurityDllCommand = new RelayCommand(BrowseSecurityDll, () => !IsBusy);
        SaveSettingsCommand = new RelayCommand(SaveUserSettings, () => !IsBusy);
        ClearLogCommand = new RelayCommand(LogEntries.Clear, () => LogEntries.Count > 0);
        ExportLogCommand = new RelayCommand(ExportLog, () => LogEntries.Count > 0);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && !IsConnected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => !IsBusy && IsConnected);
        StartFlashCommand = new AsyncRelayCommand(StartFlashAsync, () => !IsBusy && IsConnected);
        CancelFlashCommand = new RelayCommand(CancelFlash, () => IsBusy);

        LoadProfile();
        LoadUserSettings();
    }

    public ObservableCollection<string> DeviceTypes { get; }
    public ObservableCollection<string> CallingConventions { get; }
    public ObservableCollection<string> ApplicationFiles { get; }
    public ObservableCollection<FlashStepStatusRow> FlashSteps { get; }
    public ObservableCollection<string> LogEntries { get; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand BrowseDriverCommand { get; }
    public RelayCommand BrowseApplicationCommand { get; }
    public RelayCommand RemoveApplicationCommand { get; }
    public RelayCommand BrowseSecurityDllCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand ExportLogCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand StartFlashCommand { get; }
    public RelayCommand CancelFlashCommand { get; }

    public string ProductDisplayName => _profile.DisplayName;
    public string ProductId => _profile.ProductId;
    public string ProfileSummary
    {
        get => _profileSummary;
        private set => SetProperty(ref _profileSummary, value);
    }

    public string SelectedDeviceType
    {
        get => _selectedDeviceType;
        set
        {
            if (SetProperty(ref _selectedDeviceType, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string DeviceIndexText
    {
        get => _deviceIndexText;
        set => SetProperty(ref _deviceIndexText, value);
    }

    public string ChannelText
    {
        get => _channelText;
        set => SetProperty(ref _channelText, value);
    }

    public string BaudRateText
    {
        get => _baudRateText;
        set => SetProperty(ref _baudRateText, value);
    }

    public string PhysicalRequestId => _profile.Transport.PhysicalRequestId;
    public string FunctionalRequestId => _profile.Transport.FunctionalRequestId;
    public string ResponseId => _profile.Transport.ResponseId;
    public string FrameModeText => _profile.Transport.ExtendedFrame ? "扩展帧" : "标准帧";
    public string IntegrationNote => _profile.IntegrationNote;
    public bool CanEditConnectionSettings => !IsConnected && !IsBusy;
    public bool CanEditFlashSettings => !IsBusy;

    public string DriverFilePath
    {
        get => _driverFilePath;
        set => SetProperty(ref _driverFilePath, value);
    }

    public string? SelectedApplicationPath
    {
        get => _selectedApplicationPath;
        set
        {
            if (SetProperty(ref _selectedApplicationPath, value))
            {
                RemoveApplicationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SecurityDllPath
    {
        get => _securityDllPath;
        set => SetProperty(ref _securityDllPath, value);
    }

    public string AlgorithmName
    {
        get => _algorithmName;
        set => SetProperty(ref _algorithmName, value);
    }

    public string EntryPoint
    {
        get => _entryPoint;
        set => SetProperty(ref _entryPoint, value);
    }

    public string CallingConvention
    {
        get => _callingConvention;
        set => SetProperty(ref _callingConvention, value);
    }

    public string SecurityLevelText
    {
        get => _securityLevelText;
        set => SetProperty(ref _securityLevelText, value);
    }

    public string Variant
    {
        get => _variant;
        set => SetProperty(ref _variant, value);
    }

    public string OptionsText
    {
        get => _optionsText;
        set => SetProperty(ref _optionsText, value);
    }

    public string P2ClientMsText
    {
        get => _p2ClientMsText;
        set => SetProperty(ref _p2ClientMsText, value);
    }

    public string P2StarClientMsText
    {
        get => _p2StarClientMsText;
        set => SetProperty(ref _p2StarClientMsText, value);
    }

    public string S3ClientMsText
    {
        get => _s3ClientMsText;
        set => SetProperty(ref _s3ClientMsText, value);
    }

    public string PendingOverallTimeoutMsText
    {
        get => _pendingOverallTimeoutMsText;
        set => SetProperty(ref _pendingOverallTimeoutMsText, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(CanEditConnectionSettings));
                OnPropertyChanged(nameof(CanEditFlashSettings));
                RaiseCommandStates();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEditConnectionSettings));
                RaiseCommandStates();
            }
        }
    }

    public int Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
    }

    public string CurrentStep
    {
        get => _currentStep;
        private set => SetProperty(ref _currentStep, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            StatusText = "正在连接通信设备...";
            var deviceOptions = new CanDeviceOptions
            {
                DeviceType = SelectedDeviceType,
                DeviceIndex = ParseUInt(DeviceIndexText, "设备索引"),
                Channel = ParseUInt(ChannelText, "CAN 通道"),
                BaudRate = HexUtil.ParseBaudRate(BaudRateText)
            };

            await _session.ConnectAsync(deviceOptions, cancellationToken).ConfigureAwait(true);
            IsConnected = true;
            StatusText = "通信设备已连接";
            AppendLog($"已连接 {SelectedDeviceType}，通道 {deviceOptions.Channel}，波特率 {deviceOptions.BaudRate}");
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败：{ex.Message}";
            AppendLog(StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            await _session.DisconnectAsync(cancellationToken).ConfigureAwait(true);
            IsConnected = false;
            StatusText = "通信设备已断开";
            AppendLog(StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"断开失败：{ex.Message}";
            AppendLog(StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StartFlashAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            return;
        }

        using var flashCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _flashCts = flashCts;
        try
        {
            IsBusy = true;
            Progress = 0;
            CurrentStep = "准备刷写";
            SaveUserSettings();
            StatusText = "正在刷写...";

            var timing = BuildTimingOptions();
            var bootConfig = LoadBootConfig();
            FlashSteps.Clear();
            foreach (var step in bootConfig.Flow)
            {
                FlashSteps.Add(new FlashStepStatusRow { Id = step.Id, Name = step.Name });
            }

            var sessionOptions = new FlashSessionOptions
            {
                BootConfig = bootConfig,
                Project = new ProjectConfigEntry
                {
                    ProjectName = _profile.DisplayName,
                    PhysicalRequestId = _profile.Transport.PhysicalRequestId,
                    FunctionalRequestId = _profile.Transport.FunctionalRequestId,
                    ResponseAddressId = _profile.Transport.ResponseId,
                    BootConfigFile = _profile.FlowFile
                },
                Device = new CanDeviceOptions
                {
                    DeviceType = SelectedDeviceType,
                    DeviceIndex = ParseUInt(DeviceIndexText, "设备索引"),
                    Channel = ParseUInt(ChannelText, "CAN 通道"),
                    BaudRate = HexUtil.ParseBaudRate(BaudRateText)
                },
                Transport = new DiagnosticTransportOptions
                {
                    PhysicalRequestId = ParseCanId(_profile.Transport.PhysicalRequestId),
                    FunctionalRequestId = ParseCanId(_profile.Transport.FunctionalRequestId),
                    ResponseId = ParseCanId(_profile.Transport.ResponseId),
                    Channel = ParseUInt(ChannelText, "CAN 通道"),
                    ExtendedFrame = _profile.Transport.ExtendedFrame,
                    FlowControlBlockSize = _profile.Transport.FlowControlBlockSize,
                    FlowControlStMinMs = _profile.Transport.FlowControlStMinMs
                },
                Timing = timing,
                DriverFilePath = NullIfWhiteSpace(DriverFilePath),
                ApplicationFilePaths = ApplicationFiles.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray(),
                RequireDriverFile = _profile.Firmware.Any(slot => slot.Required && string.Equals(slot.Kind, "Driver", StringComparison.OrdinalIgnoreCase)),
                RequireApplicationFile = _profile.Firmware.Any(slot => slot.Required && string.Equals(slot.Kind, "Application", StringComparison.OrdinalIgnoreCase)),
                DriverFallbackAddress = ParseOptionalCanId(_profile.Firmware.FirstOrDefault(slot => string.Equals(slot.Kind, "Driver", StringComparison.OrdinalIgnoreCase))?.FallbackAddress),
                ApplicationFallbackAddress = ParseOptionalCanId(_profile.Firmware.FirstOrDefault(slot => string.Equals(slot.Kind, "Application", StringComparison.OrdinalIgnoreCase))?.FallbackAddress),
                SeedKeyDll = BuildSeedKeyOptions()
            };

            var progress = new Progress<FlashProgress>(item =>
            {
                Progress = item.Percent;
                CurrentStep = item.Message;
                UpdateStepStatus(item.Message);
            });
            var result = await _session.FlashAsync(sessionOptions, progress, AppendLog, flashCts.Token).ConfigureAwait(true);
            Progress = result.Success ? 100 : Progress;
            if (result.Success)
            {
                foreach (var row in FlashSteps)
                {
                    row.Status = "完成";
                }
            }
            else
            {
                MarkActiveStep("失败");
            }

            StatusText = result.Success ? "刷写完成" : $"刷写失败：{result.UserMessage}";
            AppendLog(StatusText);
        }
        catch (OperationCanceledException)
        {
            MarkActiveStep("已取消");
            StatusText = "刷写已取消";
            AppendLog(StatusText);
        }
        catch (Exception ex)
        {
            MarkActiveStep("失败");
            StatusText = $"刷写失败：{ex.Message}";
            AppendLog(StatusText);
        }
        finally
        {
            _flashCts = null;
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _flashCts?.Cancel();
        LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
        await _session.DisposeAsync().ConfigureAwait(false);
    }

    private void LoadProfile()
    {
        try
        {
            if (File.Exists(_profilePath))
            {
                _profile = JsonSerializer.Deserialize<EcuProductProfile>(File.ReadAllText(_profilePath), JsonOptions)
                    ?? throw new InvalidDataException("ECU 产品配置为空。");
            }

            ApplyProfileDefaults();
            ProfileSummary = $"{_profile.ProductId} | {_profile.Transport.Bus} | {_profile.Transport.ResponseId}";
            OnPropertyChanged(nameof(ProductDisplayName));
            OnPropertyChanged(nameof(ProductId));
            OnPropertyChanged(nameof(PhysicalRequestId));
            OnPropertyChanged(nameof(FunctionalRequestId));
            OnPropertyChanged(nameof(ResponseId));
            OnPropertyChanged(nameof(FrameModeText));
            OnPropertyChanged(nameof(IntegrationNote));
            LoadFlashSteps();
            StatusText = "已加载产品配置";
        }
        catch (Exception ex)
        {
            StatusText = $"产品配置加载失败：{ex.Message}";
            AppendLog(StatusText);
        }
    }

    private void ApplyProfileDefaults()
    {
        SelectedDeviceType = _profile.Transport.DeviceType;
        DeviceIndexText = _profile.Transport.DeviceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ChannelText = _profile.Transport.Channel.ToString(System.Globalization.CultureInfo.InvariantCulture);
        BaudRateText = _profile.Transport.BaudRate >= 1000
            ? $"{_profile.Transport.BaudRate / 1000}K"
            : _profile.Transport.BaudRate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        P2ClientMsText = _profile.Timing.P2ClientMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        P2StarClientMsText = _profile.Timing.P2StarClientMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        S3ClientMsText = _profile.Timing.S3ClientMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PendingOverallTimeoutMsText = _profile.Timing.PendingOverallTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AlgorithmName = _profile.Security.AlgorithmName;
        EntryPoint = _profile.Security.EntryPoint;
        CallingConvention = _profile.Security.CallingConvention;
        SecurityLevelText = _profile.Security.SecurityLevel;
        Variant = _profile.Security.Variant;
        OptionsText = _profile.Security.Options;
        OnPropertyChanged(nameof(ProductDisplayName));
    }

    private void LoadUserSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<UserProductSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            if (settings is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings.DeviceType) && DeviceTypes.Contains(settings.DeviceType))
            {
                SelectedDeviceType = settings.DeviceType;
            }

            DeviceIndexText = settings.DeviceIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? DeviceIndexText;
            ChannelText = settings.Channel?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ChannelText;
            BaudRateText = settings.BaudRate is { } baud ? FormatBaudRate(baud) : BaudRateText;
            DriverFilePath = settings.DriverFilePath ?? DriverFilePath;
            SecurityDllPath = settings.SecurityDllPath ?? SecurityDllPath;
            AlgorithmName = settings.AlgorithmName ?? AlgorithmName;
            EntryPoint = settings.EntryPoint ?? EntryPoint;
            CallingConvention = settings.CallingConvention ?? CallingConvention;
            SecurityLevelText = settings.SecurityLevel ?? SecurityLevelText;
            Variant = settings.Variant ?? Variant;
            OptionsText = settings.Options ?? OptionsText;
            P2ClientMsText = settings.P2ClientMs?.ToString() ?? P2ClientMsText;
            P2StarClientMsText = settings.P2StarClientMs?.ToString() ?? P2StarClientMsText;
            S3ClientMsText = settings.S3ClientMs?.ToString() ?? S3ClientMsText;
            PendingOverallTimeoutMsText = settings.PendingOverallTimeoutMs?.ToString() ?? PendingOverallTimeoutMsText;
            ApplicationFiles.Clear();
            foreach (var path in settings.ApplicationFilePaths ?? [])
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    ApplicationFiles.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"用户设置加载失败：{ex.Message}");
        }
    }

    private void SaveUserSettings()
    {
        try
        {
            var settings = new UserProductSettings
            {
                DeviceType = SelectedDeviceType,
                DeviceIndex = ParseUInt(DeviceIndexText, "设备索引"),
                Channel = ParseUInt(ChannelText, "CAN 通道"),
                BaudRate = HexUtil.ParseBaudRate(BaudRateText),
                DriverFilePath = NullIfWhiteSpace(DriverFilePath),
                ApplicationFilePaths = ApplicationFiles.ToList(),
                SecurityDllPath = NullIfWhiteSpace(SecurityDllPath),
                AlgorithmName = NullIfWhiteSpace(AlgorithmName),
                EntryPoint = NullIfWhiteSpace(EntryPoint),
                CallingConvention = CallingConvention,
                SecurityLevel = NullIfWhiteSpace(SecurityLevelText),
                Variant = NullIfWhiteSpace(Variant),
                Options = NullIfWhiteSpace(OptionsText),
                P2ClientMs = ParseInt(P2ClientMsText, "P2 时间"),
                P2StarClientMs = ParseInt(P2StarClientMsText, "P2* 时间"),
                S3ClientMs = ParseInt(S3ClientMsText, "S3 时间"),
                PendingOverallTimeoutMs = ParseInt(PendingOverallTimeoutMsText, "Pending 总超时")
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            StatusText = "用户配置已保存";
        }
        catch (Exception ex)
        {
            StatusText = $"配置保存失败：{ex.Message}";
            AppendLog(StatusText);
        }
    }

    private BootConfig LoadBootConfig()
    {
        var path = Path.IsPathRooted(_profile.FlowFile)
            ? _profile.FlowFile
            : Path.Combine(AppContext.BaseDirectory, "resources", "product", _profile.FlowFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("BOOT 流程配置不存在。", path);
        }

        var config = JsonSerializer.Deserialize<BootConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("BOOT 流程配置为空。");
        config.SourcePath = path;
        return config;
    }

    private void LoadFlashSteps()
    {
        var config = LoadBootConfig();
        FlashSteps.Clear();
        foreach (var step in config.Flow)
        {
            FlashSteps.Add(new FlashStepStatusRow { Id = step.Id, Name = step.Name });
        }
    }

    private UdsTimingOptions BuildTimingOptions()
    {
        return new UdsTimingOptions
        {
            P2ClientMs = ParseInt(P2ClientMsText, "P2 时间"),
            P2StarClientMs = ParseInt(P2StarClientMsText, "P2* 时间"),
            S3ClientMs = ParseInt(S3ClientMsText, "S3 时间"),
            PendingOverallTimeoutMs = ParseInt(PendingOverallTimeoutMsText, "Pending 总超时")
        }.Validate();
    }

    private SeedKeyDllOptions? BuildSeedKeyOptions()
    {
        if (string.IsNullOrWhiteSpace(SecurityDllPath))
        {
            return null;
        }

        return new SeedKeyDllOptions
        {
            AlgorithmName = string.IsNullOrWhiteSpace(AlgorithmName) ? _profile.Security.AlgorithmName : AlgorithmName,
            DllPath = SecurityDllPath,
            WorkerPath = _profile.Security.WorkerFile,
            EntryPoint = string.IsNullOrWhiteSpace(EntryPoint) ? _profile.Security.EntryPoint : EntryPoint,
            CallingConvention = CallingConvention,
            SecurityLevel = SeedKeyDllOptions.ParseSecurityLevel(SecurityLevelText),
            Variant = Variant,
            Options = OptionsText
        };
    }

    private void BrowseDriver()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "固件文件 (*.s19;*.srec;*.mot;*.hex;*.bin)|*.s19;*.srec;*.mot;*.hex;*.bin|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            DriverFilePath = dialog.FileName;
        }
    }

    private void BrowseApplication()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "固件文件 (*.s19;*.srec;*.mot;*.hex;*.bin)|*.s19;*.srec;*.mot;*.hex;*.bin|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames.Where(path => !ApplicationFiles.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            ApplicationFiles.Add(path);
        }

        SelectedApplicationPath = ApplicationFiles.LastOrDefault();
    }

    private void RemoveSelectedApplication()
    {
        if (SelectedApplicationPath is null)
        {
            return;
        }

        ApplicationFiles.Remove(SelectedApplicationPath);
        SelectedApplicationPath = ApplicationFiles.LastOrDefault();
    }

    private void BrowseSecurityDll()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "动态库 (*.dll)|*.dll|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            SecurityDllPath = dialog.FileName;
        }
    }

    private void CancelFlash() => _flashCts?.Cancel();

    private void ExportLog()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"ECU-X-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllLines(dialog.FileName, LogEntries);
            StatusText = "运行日志已导出";
        }
        catch (Exception ex)
        {
            StatusText = $"日志导出失败：{ex.Message}";
        }
    }

    private void UpdateStepStatus(string message)
    {
        if (!TryParseStepId(message, out var stepId))
        {
            return;
        }

        var row = FlashSteps.FirstOrDefault(item => item.Id == stepId);
        if (row is null)
        {
            return;
        }

        if (message.Contains("completed", StringComparison.OrdinalIgnoreCase))
        {
            row.Status = "完成";
            return;
        }

        foreach (var activeRow in FlashSteps.Where(item => item.Status == "执行中" && item.Id != stepId))
        {
            activeRow.Status = "完成";
        }

        row.Status = "执行中";
    }

    private void MarkActiveStep(string status)
    {
        var active = FlashSteps.FirstOrDefault(item => item.Status == "执行中");
        if (active is not null)
        {
            active.Status = status;
        }
    }

    private static bool TryParseStepId(string message, out int stepId)
    {
        stepId = 0;
        if (!message.StartsWith("Step ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var idStart = "Step ".Length;
        var idEnd = message.IndexOfAny([':', ' '], idStart);
        if (idEnd < 0)
        {
            idEnd = message.Length;
        }

        return int.TryParse(
            message.AsSpan(idStart, idEnd - idStart),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out stepId);
    }

    private void AppendLog(string message)
    {
        void Add()
        {
            var entry = HasTimestampPrefix(message)
                ? message
                : $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogEntries.Add(entry);
            while (LogEntries.Count > 300)
            {
                LogEntries.RemoveAt(0);
            }
        }

        if (WpfApplication.Current.Dispatcher.CheckAccess())
        {
            Add();
        }
        else
        {
            _ = WpfApplication.Current.Dispatcher.BeginInvoke(Add);
        }
    }

    private void LogEntries_CollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ClearLogCommand.RaiseCanExecuteChanged();
        ExportLogCommand.RaiseCanExecuteChanged();
    }

    private static bool HasTimestampPrefix(string message)
    {
        return message.Length >= 10
            && message[0] == '['
            && message[3] == ':'
            && message[6] == ':'
            && message[9] == ']';
    }

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        StartFlashCommand.RaiseCanExecuteChanged();
        CancelFlashCommand.RaiseCanExecuteChanged();
        RemoveApplicationCommand.RaiseCanExecuteChanged();
        SaveSettingsCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        ClearLogCommand.RaiseCanExecuteChanged();
        ExportLogCommand.RaiseCanExecuteChanged();
    }

    private static uint ParseCanId(string value) => HexUtil.ParseUInt32(value);

    private static uint ParseOptionalCanId(string? value)
        => string.IsNullOrWhiteSpace(value) ? 0 : HexUtil.ParseUInt32(value);

    private static uint ParseUInt(string value, string name)
    {
        if (!uint.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException($"{name}必须是非负整数。");
        }

        return result;
    }

    private static int ParseInt(string value, string name)
    {
        if (!int.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException($"{name}必须是整数。");
        }

        return result;
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatBaudRate(uint baudRate)
    {
        return baudRate >= 1000 && baudRate % 1000 == 0
            ? $"{baudRate / 1000}K"
            : baudRate.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
