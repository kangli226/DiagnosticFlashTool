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
        set => SetProperty(ref _result, value);
    }

    public void MarkCompleted(string currentValue, DateTime detectionDate)
    {
        CurrentValue = currentValue;
        DetectionDate = detectionDate.ToString("yyyy-MM-dd HH:mm:ss");
        Result = string.Equals(CurrentValue, ExpectedValue, StringComparison.OrdinalIgnoreCase) ? "通过" : "不通过";
    }
}
