using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DiagnosticFlashTool.App.Views.Controls;

/// <summary>
/// 将路径的主要打开操作与更改路径操作组合到一个分裂按钮中。
/// </summary>
public partial class PathActionSplitButton : UserControl
{
    public static readonly DependencyProperty PrimaryCommandProperty = DependencyProperty.Register(
        nameof(PrimaryCommand),
        typeof(ICommand),
        typeof(PathActionSplitButton));

    public static readonly DependencyProperty PrimaryLabelProperty = DependencyProperty.Register(
        nameof(PrimaryLabel),
        typeof(string),
        typeof(PathActionSplitButton),
        new PropertyMetadata("打开位置"));

    public static readonly DependencyProperty ChangeCommandProperty = DependencyProperty.Register(
        nameof(ChangeCommand),
        typeof(ICommand),
        typeof(PathActionSplitButton));

    public static readonly DependencyProperty ChangeLabelProperty = DependencyProperty.Register(
        nameof(ChangeLabel),
        typeof(string),
        typeof(PathActionSplitButton),
        new PropertyMetadata("更改路径…"));

    public PathActionSplitButton()
    {
        InitializeComponent();
    }

    public ICommand? PrimaryCommand
    {
        get => (ICommand?)GetValue(PrimaryCommandProperty);
        set => SetValue(PrimaryCommandProperty, value);
    }

    public string PrimaryLabel
    {
        get => (string)GetValue(PrimaryLabelProperty);
        set => SetValue(PrimaryLabelProperty, value);
    }

    public ICommand? ChangeCommand
    {
        get => (ICommand?)GetValue(ChangeCommandProperty);
        set => SetValue(ChangeCommandProperty, value);
    }

    public string ChangeLabel
    {
        get => (string)GetValue(ChangeLabelProperty);
        set => SetValue(ChangeLabelProperty, value);
    }

    private void OnChangeMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } contextMenu } button)
        {
            return;
        }

        contextMenu.PlacementTarget = button;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }
}
