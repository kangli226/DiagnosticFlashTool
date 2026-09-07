using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed class FlowScriptEditorRow : ObservableObject
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _type = "lua";
    private string _description = string.Empty;
    private string _code = string.Empty;
    private string _parameterNamesText = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    public string ParameterNamesText
    {
        get => _parameterNamesText;
        set => SetProperty(ref _parameterNamesText, value);
    }

    public static FlowScriptEditorRow FromConfig(FlowScriptConfig script)
    {
        return new FlowScriptEditorRow
        {
            Id = script.Id,
            Name = script.Name,
            Type = script.Type,
            Description = script.Description,
            Code = script.Code,
            ParameterNamesText = string.Join(", ", script.ParameterNames)
        };
    }

    public FlowScriptConfig ToConfig()
    {
        return new FlowScriptConfig
        {
            Id = Id.Trim(),
            Name = Name.Trim(),
            Type = Type.Trim(),
            Description = Description.Trim(),
            Code = Code,
            ParameterNames = ParameterNamesText
                .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList()
        };
    }
}
