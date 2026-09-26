using System.Windows;
using System.Windows.Controls;

namespace DiagnosticFlashTool.App.Views.Controls;

public partial class PageHeader : UserControl
{
    /// <summary>页头单行上下文文本；为空时页头仅显示操作区。</summary>
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context),
        typeof(string),
        typeof(PageHeader),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions),
        typeof(object),
        typeof(PageHeader),
        new PropertyMetadata(null));

    public PageHeader()
    {
        InitializeComponent();
    }

    public string Context
    {
        get => (string)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
