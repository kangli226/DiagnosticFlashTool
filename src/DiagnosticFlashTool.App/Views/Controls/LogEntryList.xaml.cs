using System.Collections;
using System.Windows;
using System.Windows.Controls;

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

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }
}
