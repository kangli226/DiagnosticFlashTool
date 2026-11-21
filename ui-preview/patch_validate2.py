# -*- coding: utf-8 -*-
import io, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
P = os.path.join(ROOT, "src/DiagnosticFlashTool.App/ViewModels/MainViewModel.cs")
fails = []
s = io.open(P, encoding="utf-8").read()
def rep(old, new, n=1, tag=""):
    global s
    eol = "\r\n" if "\r\n" in s else "\n"
    o = old.replace("\n", eol); nw = new.replace("\n", eol)
    c = s.count(o)
    if c != n:
        fails.append("%s: expected %d got %d" % (tag, n, c)); return
    s = s.replace(o, nw)

METHODS = '''    private void SetFlowValidationNotice(string message)
    {
        _hasFlowValidationResult = false;
        FlowValidationLines.Clear();
        FlowValidationLines.Add(new FlowValidationLine(FlowValidationSeverity.Info, message));
        FlowValidationText = message;
        RaiseFlowValidationChanged();
    }

    private void ApplyFlowValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        _hasFlowValidationResult = true;
        FlowValidationLines.Clear();
        foreach (var error in errors)
        {
            FlowValidationLines.Add(new FlowValidationLine(FlowValidationSeverity.Error, error));
        }

        foreach (var warning in warnings)
        {
            FlowValidationLines.Add(new FlowValidationLine(FlowValidationSeverity.Warning, warning));
        }

        FlowValidationText = errors.Count == 0 && warnings.Count == 0
            ? $"校验通过：共 {FlowRows.Count} 个流程节点。"
            : string.Join(
                Environment.NewLine,
                errors.Select(text => $"错误：{text}").Concat(warnings.Select(text => $"提示：{text}")));
        RaiseFlowValidationChanged();
    }

    private void RaiseFlowValidationChanged()
    {
        OnPropertyChanged(nameof(HasFlowValidationResult));
        OnPropertyChanged(nameof(FlowValidationErrorCount));
        OnPropertyChanged(nameof(FlowValidationWarningCount));
        OnPropertyChanged(nameof(FlowValidationStateKind));
        OnPropertyChanged(nameof(FlowValidationStatusText));
        OnPropertyChanged(nameof(FlowValidationSummaryText));
    }

    private void UpdateExecutingFlowRow()
    {
        foreach (var row in FlowRows)
        {
            row.IsExecuting = _currentExecutingStepId is { } stepId && row.Id == stepId;
        }
    }

'''
rep("    private IReadOnlyList<FlashFlowIssue> ValidateAppFlow(BootConfig config)",
    METHODS + "    private IReadOnlyList<FlashFlowIssue> ValidateAppFlow(BootConfig config)", tag="methods")

rep('''        FlowValidationText = issues.Count == 0 && warnings.Count == 0
            ? $"校验通过：共 {FlowRows.Count} 个流程节点。"
            : string.Join(
                Environment.NewLine,
                issues.Select(text => $"错误：{text}").Concat(warnings.Select(text => $"提示：{text}")));''',
    "        ApplyFlowValidationResult(issues, warnings);", tag="apply-result")

rep('            FlowValidationText = "未选择项目。";', '            SetFlowValidationNotice("未选择项目。");', tag="notice-1")
rep('            FlowValidationText = "请选择 BOOT 配置文件。";', '            SetFlowValidationNotice("请选择 BOOT 配置文件。");', tag="notice-2")
rep('            FlowValidationText = ex.Message;', '            SetFlowValidationNotice(ex.Message);', tag="notice-3")

rep('''                Progress = Math.Clamp(item.Percent, 0, 100);
                DownloadStatusKind = DiagnosticStatusKind.Running;
                SetStatus(item.Message, DiagnosticStatusKind.Running);''',
    '''                Progress = Math.Clamp(item.Percent, 0, 100);
                DownloadStatusKind = DiagnosticStatusKind.Running;
                CurrentExecutingStepId = item.StepId;
                SetStatus(item.Message, DiagnosticStatusKind.Running);''', tag="progress-stepid")

rep('''                progress,
                cancellationToken);

            DownloadStatusKind = result.Success ? DiagnosticStatusKind.Success : DiagnosticStatusKind.Error;''',
    '''                progress,
                cancellationToken);

            CurrentExecutingStepId = null;
            DownloadStatusKind = result.Success ? DiagnosticStatusKind.Success : DiagnosticStatusKind.Error;''', tag="reset-after")

rep('''        catch (OperationCanceledException)
        {
            DownloadStatusKind = DiagnosticStatusKind.Warning;
            SetStatus("刷写已取消", DiagnosticStatusKind.Warning);''',
    '''        catch (OperationCanceledException)
        {
            CurrentExecutingStepId = null;
            DownloadStatusKind = DiagnosticStatusKind.Warning;
            SetStatus("刷写已取消", DiagnosticStatusKind.Warning);''', tag="reset-cancel")

rep('''        catch (Exception ex)
        {
            DownloadStatusKind = DiagnosticStatusKind.Error;
            SetStatus(ex.Message, DiagnosticStatusKind.Error);''',
    '''        catch (Exception ex)
        {
            CurrentExecutingStepId = null;
            DownloadStatusKind = DiagnosticStatusKind.Error;
            SetStatus(ex.Message, DiagnosticStatusKind.Error);''', tag="reset-error")

if fails:
    print("FAILED")
    for f in fails: print(" -", f)
    sys.exit(1)
io.open(P, "w", encoding="utf-8", newline="").write(s)
print("vm logic patched")
