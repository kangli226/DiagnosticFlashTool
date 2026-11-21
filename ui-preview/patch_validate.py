# -*- coding: utf-8 -*-
import io, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "src/DiagnosticFlashTool.App")
CORE = os.path.join(ROOT, "src/DiagnosticFlashTool.Core")
fails = []

def read(p):
    return io.open(p, encoding="utf-8").read()

def write(p, s):
    io.open(p, "w", encoding="utf-8", newline="").write(s)

def rep(s, old, new, n=1, tag=""):
    eol = "\r\n" if "\r\n" in s else "\n"
    o = old.replace("\n", eol); nw = new.replace("\n", eol)
    c = s.count(o)
    if c != n:
        fails.append("%s: expected %d got %d" % (tag, n, c)); return s
    return s.replace(o, nw)

# ---------- Core: FlashProgress 带上步骤号 ----------
p = os.path.join(CORE, "Flashing/FlashProgress.cs")
s = read(p)
s = rep(s, "public sealed record FlashProgress(int Percent, string Message);",
"""/// <summary>
/// 刷写进度。StepId 为当前正在执行的流程节点号，供界面高亮对应行；
/// 开始、完成等非步骤级进度为 null。
/// </summary>
public sealed record FlashProgress(int Percent, string Message, int? StepId = null);""", tag="FlashProgress")
write(p, s)

# ---------- Core: 上报步骤号 ----------
p = os.path.join(CORE, "Flashing/FlashFlowExecutor.cs")
s = read(p)
s = rep(s, """                    progress?.Report(new FlashProgress(
                        Math.Clamp((int)Math.Round(overall * 100), 0, 100),
                        message));""",
"""                    progress?.Report(new FlashProgress(
                        Math.Clamp((int)Math.Round(overall * 100), 0, 100),
                        message,
                        step.Id));""", tag="executor-stepid")
write(p, s)

# ---------- App: 新增校验行模型 ----------
p = os.path.join(APP, "ViewModels/FlowValidationLine.cs")
write(p, '''namespace DiagnosticFlashTool.App.ViewModels;

public enum FlowValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 校验结果的一行：严重级别 + 文案。界面按级别取图标与语义色，
/// 同时保留图标和文字，符合"状态不能只靠颜色表达"的规范要求。
/// </summary>
public sealed class FlowValidationLine(FlowValidationSeverity severity, string text)
{
    public FlowValidationSeverity Severity { get; } = severity;

    public string Text { get; } = text;

    public bool IsError => Severity == FlowValidationSeverity.Error;

    public bool IsWarning => Severity == FlowValidationSeverity.Warning;

    public string Icon => Severity switch
    {
        FlowValidationSeverity.Error => "\\u2715",
        FlowValidationSeverity.Warning => "!",
        _ => "\\u2022"
    };
}
''')

# ---------- App: FlowStepEditorRow 增加执行态 ----------
p = os.path.join(APP, "ViewModels/FlowStepEditorRow.cs")
s = read(p)
s = rep(s, "    private int _id;", "    private int _id;\n    private bool _isExecuting;", tag="row-field")
s = rep(s, """    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }""",
"""    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    /// <summary>刷写执行到该节点时为 true，表格按行样式高亮。</summary>
    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetProperty(ref _isExecuting, value);
    }""", tag="row-prop")
write(p, s)

# ---------- App: MainViewModel ----------
p = os.path.join(APP, "ViewModels/MainViewModel.cs")
s = read(p)
s = rep(s, '    private string _flowValidationText = "未加载流程配置。";',
        '    private string _flowValidationText = "未加载流程配置。";\n    private bool _hasFlowValidationResult;\n    private int? _currentExecutingStepId;', tag="vm-fields")
s = rep(s, """    public string FlowValidationText
    {
        get => _flowValidationText;
        set => SetProperty(ref _flowValidationText, value);
    }""",
"""    public string FlowValidationText
    {
        get => _flowValidationText;
        set => SetProperty(ref _flowValidationText, value);
    }

    public ObservableCollection<FlowValidationLine> FlowValidationLines { get; } = [];

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

    public string FlowValidationSummaryText => !_hasFlowValidationResult
        ? "点击「校验」检查当前流程配置"
        : FlowValidationErrorCount == 0 && FlowValidationWarningCount == 0
            ? $"共 {FlowRows.Count} 个流程节点"
            : string.Join(" · ", new[]
            {
                FlowValidationErrorCount > 0 ? $"{FlowValidationErrorCount} 项错误" : null,
                FlowValidationWarningCount > 0 ? $"{FlowValidationWarningCount} 项提示" : null
            }.Where(part => part is not null));

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
    }""", tag="vm-props")
write(p, s)

if fails:
    print("FAILED:")
    for f in fails:
        print(" -", f)
    sys.exit(1)
print("core+model patch applied")
