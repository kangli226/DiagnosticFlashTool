namespace DiagnosticFlashTool.Product.EcuX.ViewModels;

public sealed class FlashStepStatusRow : ObservableObject
{
    private string _status = "待执行";

    public required int Id { get; init; }
    public required string Name { get; init; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
