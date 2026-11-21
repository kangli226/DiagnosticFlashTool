# -*- coding: utf-8 -*-
import io, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "src/DiagnosticFlashTool.App")
fails = []
def read(p): return io.open(p, encoding="utf-8").read()
def write(p, s): io.open(p, "w", encoding="utf-8", newline="").write(s)
def rep(s, old, new, n=1, tag=""):
    eol = "\r\n" if "\r\n" in s else "\n"
    o = old.replace("\n", eol); nw = new.replace("\n", eol)
    c = s.count(o)
    if c != n:
        fails.append("%s: expected %d got %d" % (tag, n, c)); return s
    return s.replace(o, nw)

# 1) 行样式只对执行行生效
p = os.path.join(APP, "Styles/Controls/DataGridStyles.xaml")
s = read(p)
s = rep(s, """    <Style x:Key="DataGrid.RowRunning" TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
        <Setter Property="Background" Value="{StaticResource PrimarySoftBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource PrimaryBrush}" />
        <Setter Property="BorderThickness" Value="3,0,0,0" />
    </Style>""",
"""    <Style x:Key="DataGrid.RowRunning" TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsExecuting}" Value="True">
                <Setter Property="Background" Value="{StaticResource PrimarySoftBrush}" />
                <Setter Property="BorderBrush" Value="{StaticResource PrimaryBrush}" />
                <Setter Property="BorderThickness" Value="3,0,0,0" />
            </DataTrigger>
        </Style.Triggers>
    </Style>""", tag="rowstyle-trigger")
write(p, s)

# 2) 校验卡：字面量图标修正 + 高度自适应
p = os.path.join(APP, "Views/FlowConfigView.xaml")
s = read(p)
s = rep(s, '                        <TextBlock Text="\\u25cf" Margin="0,0,6,0" VerticalAlignment="Center">',
        '                        <TextBlock Text="\u25cf" Margin="0,0,6,0" VerticalAlignment="Center">', tag="glyph")
s = rep(s, "            <RowDefinition Height=\"104\" />",
        "            <RowDefinition Height=\"Auto\" MaxHeight=\"220\" />", tag="card-row")
s = rep(s, """                <ScrollViewer Grid.Row="1"
                              VerticalScrollBarVisibility="Hidden"
                              HorizontalScrollBarVisibility="Disabled">""",
        """                <ScrollViewer Grid.Row="1"
                              MaxHeight="150"
                              VerticalScrollBarVisibility="Hidden"
                              HorizontalScrollBarVisibility="Disabled">""", tag="scroll-max")
write(p, s)

# 3) 摘要不再与状态文字重复
p = os.path.join(APP, "ViewModels/MainViewModel.cs")
s = read(p)
s = rep(s, """    public string FlowValidationSummaryText => !_hasFlowValidationResult
        ? "点击「校验」检查当前流程配置"
        : FlowValidationErrorCount == 0 && FlowValidationWarningCount == 0
            ? $"共 {FlowRows.Count} 个流程节点"
            : string.Join(" · ", new[]
            {
                FlowValidationErrorCount > 0 ? $"{FlowValidationErrorCount} 项错误" : null,
                FlowValidationWarningCount > 0 ? $"{FlowValidationWarningCount} 项提示" : null
            }.Where(part => part is not null));""",
"""    public string FlowValidationSummaryText => _hasFlowValidationResult
        ? $"共 {FlowRows.Count} 个流程节点"
        : "点击「校验」检查当前流程配置";""", tag="summary")
write(p, s)

if fails:
    print("FAILED")
    for f in fails: print(" -", f)
    sys.exit(1)
print("fixes applied")
