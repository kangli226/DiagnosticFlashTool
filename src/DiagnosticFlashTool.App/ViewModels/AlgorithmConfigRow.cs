namespace DiagnosticFlashTool.App.ViewModels;

public sealed class AlgorithmConfigRow
{
    public AlgorithmConfigRow(string category, string name, string source, string status)
    {
        Category = category;
        Name = name;
        Source = source;
        Status = status;
    }

    public string Category { get; }
    public string Name { get; }
    public string Source { get; }
    public string Status { get; }
}
