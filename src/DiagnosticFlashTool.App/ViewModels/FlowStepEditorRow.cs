using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed class FlowStepEditorRow : ObservableObject
{
    private int _id;
    private string _name = string.Empty;
    private string _stepType = string.Empty;
    private string _service = string.Empty;
    private string _subService = string.Empty;
    private string _extendText = string.Empty;
    private string _addressingMode = "physical";
    private string _securityAlgorithm = string.Empty;
    private string _crcAlgorithm = string.Empty;
    private string _timeoutMs = string.Empty;
    private string _pendingTimeoutMs = string.Empty;
    private string _testerPresentIntervalMs = string.Empty;
    private string _algorithmParamsText = string.Empty;

    public Dictionary<string, string> Receive { get; private set; } = [];
    public JsonStringList Verify { get; private set; } = [];
    public string? BlockSize { get; private set; }
    public string? EraseRoutine { get; private set; }

    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string StepType
    {
        get => _stepType;
        set
        {
            if (SetProperty(ref _stepType, value))
            {
                OnPropertyChanged(nameof(IsDownloadStep));
                OnPropertyChanged(nameof(IsUdsStep));
                OnPropertyChanged(nameof(Algorithm));
            }
        }
    }

    public string Service
    {
        get => _service;
        set => SetProperty(ref _service, value);
    }

    public string SubService
    {
        get => _subService;
        set => SetProperty(ref _subService, value);
    }

    public string ExtendText
    {
        get => _extendText;
        set => SetProperty(ref _extendText, value);
    }

    public string AddressingMode
    {
        get => _addressingMode;
        set => SetProperty(ref _addressingMode, value);
    }

    public string SecurityAlgorithm
    {
        get => _securityAlgorithm;
        set
        {
            if (SetProperty(ref _securityAlgorithm, value))
            {
                OnPropertyChanged(nameof(Algorithm));
            }
        }
    }

    public string CrcAlgorithm
    {
        get => _crcAlgorithm;
        set
        {
            if (SetProperty(ref _crcAlgorithm, value))
            {
                OnPropertyChanged(nameof(Algorithm));
            }
        }
    }

    public bool IsDownloadStep => string.Equals(StepType, "DownloadDriver", StringComparison.OrdinalIgnoreCase)
        || string.Equals(StepType, "DownloadApplication", StringComparison.OrdinalIgnoreCase);

    public bool IsUdsStep => !IsDownloadStep;

    /// <summary>
    /// The compact editor presents the algorithm configured by this step in one column.
    /// Download steps own a CRC algorithm; regular UDS steps own a security algorithm.
    /// </summary>
    public string Algorithm
    {
        get => string.IsNullOrWhiteSpace(SecurityAlgorithm) ? CrcAlgorithm : SecurityAlgorithm;
        set
        {
            var algorithm = value?.Trim() ?? string.Empty;
            var useCrcAlgorithm = IsDownloadStep
                || (string.IsNullOrWhiteSpace(SecurityAlgorithm) && !string.IsNullOrWhiteSpace(CrcAlgorithm));

            if (useCrcAlgorithm)
            {
                var changed = !string.Equals(_crcAlgorithm, algorithm, StringComparison.Ordinal)
                    || !string.IsNullOrEmpty(_securityAlgorithm);
                _crcAlgorithm = algorithm;
                _securityAlgorithm = string.Empty;

                if (changed)
                {
                    OnPropertyChanged(nameof(CrcAlgorithm));
                    OnPropertyChanged(nameof(SecurityAlgorithm));
                    OnPropertyChanged();
                }

                return;
            }

            var securityChanged = !string.Equals(_securityAlgorithm, algorithm, StringComparison.Ordinal)
                || !string.IsNullOrEmpty(_crcAlgorithm);
            _securityAlgorithm = algorithm;
            _crcAlgorithm = string.Empty;

            if (securityChanged)
            {
                OnPropertyChanged(nameof(SecurityAlgorithm));
                OnPropertyChanged(nameof(CrcAlgorithm));
                OnPropertyChanged();
            }
        }
    }

    public string TimeoutMs
    {
        get => _timeoutMs;
        set => SetProperty(ref _timeoutMs, value);
    }

    public string PendingTimeoutMs
    {
        get => _pendingTimeoutMs;
        set => SetProperty(ref _pendingTimeoutMs, value);
    }

    public string TesterPresentIntervalMs
    {
        get => _testerPresentIntervalMs;
        set => SetProperty(ref _testerPresentIntervalMs, value);
    }

    public string AlgorithmParamsText
    {
        get => _algorithmParamsText;
        set => SetProperty(ref _algorithmParamsText, value);
    }

    public static FlowStepEditorRow FromConfig(FlashStepConfig step)
    {
        return new FlowStepEditorRow
        {
            Id = step.Id,
            Name = step.Name,
            StepType = step.StepType ?? string.Empty,
            Service = step.Service ?? string.Empty,
            SubService = step.SubService ?? string.Empty,
            ExtendText = string.Join(", ", step.Extend),
            AddressingMode = string.IsNullOrWhiteSpace(step.AddressingMode) ? "physical" : step.AddressingMode!,
            SecurityAlgorithm = step.SecurityAlgorithm ?? string.Empty,
            CrcAlgorithm = step.CrcAlgorithm ?? string.Empty,
            TimeoutMs = step.TimeoutMs ?? string.Empty,
            PendingTimeoutMs = step.PendingTimeoutMs ?? string.Empty,
            TesterPresentIntervalMs = step.TesterPresentIntervalMs ?? string.Empty,
            AlgorithmParamsText = string.Join(", ", step.AlgorithmParams),
            Receive = new Dictionary<string, string>(step.Receive),
            Verify = step.Verify,
            BlockSize = step.BlockSize,
            EraseRoutine = step.EraseRoutine
        };
    }

    public FlashStepConfig ToConfig()
    {
        return new FlashStepConfig
        {
            Id = Id,
            Name = Name,
            StepType = EmptyToNull(StepType),
            Service = EmptyToNull(Service),
            SubService = EmptyToNull(SubService),
            Extend = ToStringList(ExtendText),
            Receive = new Dictionary<string, string>(Receive),
            Verify = Verify,
            BlockSize = BlockSize,
            TimeoutMs = EmptyToNull(TimeoutMs),
            PendingTimeoutMs = EmptyToNull(PendingTimeoutMs),
            TesterPresentIntervalMs = EmptyToNull(TesterPresentIntervalMs),
            EraseRoutine = EraseRoutine,
            AddressingMode = EmptyToNull(AddressingMode),
            SecurityAlgorithm = EmptyToNull(SecurityAlgorithm),
            CrcAlgorithm = EmptyToNull(CrcAlgorithm),
            AlgorithmParams = ToStringList(AlgorithmParamsText)
        };
    }

    private static JsonStringList ToStringList(string value)
    {
        var list = new JsonStringList();
        foreach (var item in value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            list.Add(item);
        }

        return list;
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
