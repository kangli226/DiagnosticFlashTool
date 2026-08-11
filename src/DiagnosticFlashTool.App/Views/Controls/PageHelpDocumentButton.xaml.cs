using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DiagnosticFlashTool.App.Views.Controls;

public partial class PageHelpDocumentButton : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(PageHelpDocumentButton),
        new PropertyMetadata("\u5E2E\u52A9\u6587\u6863"));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(PageHelpDocumentButton),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter),
        typeof(object),
        typeof(PageHelpDocumentButton),
        new PropertyMetadata(null));

    public PageHelpDocumentButton()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }
}
