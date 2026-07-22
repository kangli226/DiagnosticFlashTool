namespace DiagnosticFlashTool.App.ViewModels;

public sealed class FunctionalCheckRow : ObservableObject
{
    private int _sequence;
    private string _name = string.Empty;
    private string _expectedValue = string.Empty;
    private string _currentValue = "NA";
    private string _detectionDate = "NA";
    private string _result = "NA";

    public FunctionalCheckRow(int sequence, string name, string expectedValue)
    {
        Sequence = sequence;
        Name = name;
        ExpectedValue = expectedValue;
    }

    public int Sequence
    {
        get => _sequence;
        set => SetProperty(ref _sequence, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string ExpectedValue
    {
        get => _expectedValue;
        set => SetProperty(ref _expectedValue, value);
    }

    public string CurrentValue
    {
        get => _currentValue;
        set => SetProperty(ref _currentValue, value);
    }

    public string DetectionDate
    {
        get => _detectionDate;
        set => SetProperty(ref _detectionDate, value);
    }

    public string Result
    {
        get => _result;
        set
        {
            if (SetProperty(ref _result, value))
            {
                OnPropertyChanged(nameof(ResultStatusKind));
            }
        }
    }

    public DiagnosticStatusKind ResultStatusKind
    {
        get
        {
            if (Result.Equals("NA", StringComparison.OrdinalIgnoreCase))
            {
                return DiagnosticStatusKind.Neutral;
            }

            if (Result.Contains("不通过", StringComparison.OrdinalIgnoreCase) ||
                Result.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
                Result.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return DiagnosticStatusKind.Error;
            }

            if (Result.Contains("通过", StringComparison.OrdinalIgnoreCase) ||
                Result.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
                Result.Contains("pass", StringComparison.OrdinalIgnoreCase))
            {
                return DiagnosticStatusKind.Success;
            }

            if (Result.Contains("检测中", StringComparison.OrdinalIgnoreCase) ||
                Result.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                return DiagnosticStatusKind.Running;
            }

            return DiagnosticStatusKind.Neutral;
        }
    }

    public void MarkCompleted(string currentValue, DateTime detectionDate)
    {
        CurrentValue = currentValue;
        DetectionDate = detectionDate.ToString("yyyy-MM-dd HH:mm:ss");
        Result = string.Equals(CurrentValue, ExpectedValue, StringComparison.OrdinalIgnoreCase) ? "通过" : "不通过";
    }
}
