using DiagnosticFlashTool.Core.Configuration;
using DiagnosticFlashTool.Core.Flashing;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed class FlowStepEditorRow : ObservableObject
{
    private int _id;
    private string _templateId = string.Empty;
    private string _name = string.Empty;
    private string _stepType = string.Empty;
    private string _service = string.Empty;
    private string _subService = string.Empty;
    private string _extendText = string.Empty;
    private string _addressingMode = "physical";
    private string _securityAlgorithm = string.Empty;
    private string _crcAlgorithm = string.Empty;
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

    public string TemplateId
    {
        get => _templateId;
        private set
        {
            if (SetProperty(ref _templateId, value))
            {
                RaiseTemplateStateChanged();
            }
        }
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
                RaiseTemplateStateChanged();
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
                OnPropertyChanged(nameof(HasUnsupportedAlgorithm));
                OnPropertyChanged(nameof(AlgorithmDisplayText));
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
                OnPropertyChanged(nameof(HasUnsupportedAlgorithm));
                OnPropertyChanged(nameof(AlgorithmDisplayText));
                OnPropertyChanged(nameof(CanEditSecurityAlgorithm));
            }
        }
    }

    public bool IsDownloadStep => FlashStepTypes.IsDownload(StepType);

    public bool IsUdsStep => !IsDownloadStep;

    public bool IsSecurityAlgorithmStep =>
        BuiltInFlowStepTemplateCatalog.FindById(TemplateId)?.AlgorithmUsage
        == FlowStepTemplateAlgorithmUsage.Security;

    public bool CanEditSecurityAlgorithm => IsSecurityAlgorithmStep && string.IsNullOrWhiteSpace(CrcAlgorithm);

    public bool HasUnsupportedAlgorithm =>
        !string.IsNullOrWhiteSpace(CrcAlgorithm)
        || (!IsSecurityAlgorithmStep
            && (!string.IsNullOrWhiteSpace(SecurityAlgorithm) || !string.IsNullOrWhiteSpace(AlgorithmParamsText)));

    public string AlgorithmDisplayText => HasUnsupportedAlgorithm
        ? $"不兼容：{FirstConfiguredAlgorithm()}（请更换模板）"
        : "不适用";

    public string TesterPresentIntervalMs
    {
        get => _testerPresentIntervalMs;
        set => SetProperty(ref _testerPresentIntervalMs, value);
    }

    public string AlgorithmParamsText
    {
        get => _algorithmParamsText;
        set
        {
            if (SetProperty(ref _algorithmParamsText, value))
            {
                OnPropertyChanged(nameof(HasUnsupportedAlgorithm));
                OnPropertyChanged(nameof(AlgorithmDisplayText));
            }
        }
    }

    public static FlowStepEditorRow FromConfig(FlashStepConfig step)
    {
        return new FlowStepEditorRow
        {
            Id = step.Id,
            TemplateId = ResolveTemplateId(step),
            Name = step.Name,
            StepType = step.StepType ?? string.Empty,
            Service = step.Service ?? string.Empty,
            SubService = step.SubService ?? string.Empty,
            ExtendText = string.Join(", ", step.Extend),
            AddressingMode = string.IsNullOrWhiteSpace(step.AddressingMode) ? "physical" : step.AddressingMode!,
            SecurityAlgorithm = step.SecurityAlgorithm ?? string.Empty,
            CrcAlgorithm = step.CrcAlgorithm ?? string.Empty,
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
            TemplateId = EmptyToNull(TemplateId),
            Id = Id,
            Name = Name,
            StepType = EmptyToNull(StepType),
            Service = EmptyToNull(Service),
            SubService = EmptyToNull(SubService),
            Extend = ToStringList(ExtendText),
            Receive = new Dictionary<string, string>(Receive),
            Verify = Verify,
            BlockSize = BlockSize,
            TesterPresentIntervalMs = EmptyToNull(TesterPresentIntervalMs),
            EraseRoutine = EraseRoutine,
            AddressingMode = EmptyToNull(AddressingMode),
            SecurityAlgorithm = EmptyToNull(SecurityAlgorithm),
            CrcAlgorithm = EmptyToNull(CrcAlgorithm),
            AlgorithmParams = ToStringList(AlgorithmParamsText)
        };
    }

    public void ApplyTemplate(FlowStepTemplateDefinition template, bool preserveCompatibleValues)
    {
        ArgumentNullException.ThrowIfNull(template);

        var wasSecurityStep = IsSecurityAlgorithmStep;
        var wasDownloadStep = IsDownloadStep;
        var securityAlgorithm = SecurityAlgorithm;
        var algorithmParams = AlgorithmParamsText;
        var blockSize = BlockSize;

        TemplateId = template.Id;
        Name = template.StepName;
        StepType = template.StepType ?? string.Empty;
        Service = template.Service ?? string.Empty;
        SubService = template.SubService ?? string.Empty;
        ExtendText = string.Join(", ", template.Extend);
        AddressingMode = "physical";

        var preserveSecurity = preserveCompatibleValues
            && wasSecurityStep
            && template.AlgorithmUsage == FlowStepTemplateAlgorithmUsage.Security;
        SecurityAlgorithm = preserveSecurity ? securityAlgorithm : string.Empty;
        AlgorithmParamsText = preserveSecurity ? algorithmParams : string.Empty;
        CrcAlgorithm = string.Empty;
        BlockSize = preserveCompatibleValues && wasDownloadStep && FlashStepTypes.IsDownload(template.StepType)
            ? blockSize
            : null;

        Receive = [];
        Verify = [];
        TesterPresentIntervalMs = string.Empty;
        EraseRoutine = null;
        RaiseTemplateStateChanged();
    }

    private static string ResolveTemplateId(FlashStepConfig step)
    {
        if (!string.IsNullOrWhiteSpace(step.TemplateId))
        {
            return BuiltInFlowStepTemplateCatalog.FindById(step.TemplateId)?.Id ?? step.TemplateId.Trim();
        }

        return BuiltInFlowStepTemplateCatalog.FindMatchingTemplate(step)?.Id ?? string.Empty;
    }

    private string FirstConfiguredAlgorithm()
    {
        if (!string.IsNullOrWhiteSpace(CrcAlgorithm))
        {
            return CrcAlgorithm;
        }

        if (!string.IsNullOrWhiteSpace(SecurityAlgorithm))
        {
            return SecurityAlgorithm;
        }

        return "算法参数";
    }

    private void RaiseTemplateStateChanged()
    {
        OnPropertyChanged(nameof(IsSecurityAlgorithmStep));
        OnPropertyChanged(nameof(CanEditSecurityAlgorithm));
        OnPropertyChanged(nameof(HasUnsupportedAlgorithm));
        OnPropertyChanged(nameof(AlgorithmDisplayText));
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
