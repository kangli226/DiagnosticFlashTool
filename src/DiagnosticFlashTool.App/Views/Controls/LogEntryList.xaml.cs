using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiagnosticFlashTool.App.ViewModels;

namespace DiagnosticFlashTool.App.Views.Controls;

public sealed partial class LogEntryList : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(LogEntryList), new PropertyMetadata(null));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(LogEntryList), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty EmptyHintTextProperty = DependencyProperty.Register(
        nameof(EmptyHintText), typeof(string), typeof(LogEntryList), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AutoScrollEnabledProperty = DependencyProperty.Register(
        nameof(AutoScrollEnabled), typeof(bool), typeof(LogEntryList), new PropertyMetadata(true));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(LogEntryList), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowProjectSourceProperty = DependencyProperty.Register(
        nameof(ShowProjectSource), typeof(bool), typeof(LogEntryList), new PropertyMetadata(false));

    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(LogEntryList),
        new PropertyMetadata(ScrollBarVisibility.Hidden));

    public LogEntryList()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public string EmptyHintText
    {
        get => (string)GetValue(EmptyHintTextProperty);
        set => SetValue(EmptyHintTextProperty, value);
    }

    public bool AutoScrollEnabled
    {
        get => (bool)GetValue(AutoScrollEnabledProperty);
        set => SetValue(AutoScrollEnabledProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool ShowProjectSource
    {
        get => (bool)GetValue(ShowProjectSourceProperty);
        set => SetValue(ShowProjectSourceProperty, value);
    }

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    private void CopyEntries_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = IsCompact && Entries.SelectedItems.Count > 0;
        e.Handled = IsCompact;
    }

    private void CopyEntries_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!IsCompact || Entries.SelectedItems.Count == 0)
        {
            return;
        }

        var selectedLines = Entries.Items.OfType<SystemLogEntry>()
            .Where(entry => Entries.SelectedItems.Contains(entry))
            .Select(entry => entry.Text);
        e.Handled = true;

        try
        {
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, selectedLines));
        }
        catch (ExternalException ex)
        {
            CommandErrorHandler.Report(ex);
        }
    }
}
