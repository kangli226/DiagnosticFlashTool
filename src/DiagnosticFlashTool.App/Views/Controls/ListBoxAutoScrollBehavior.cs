using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using WpfListBox = System.Windows.Controls.ListBox;

namespace DiagnosticFlashTool.App.Views.Controls;

public static class ListBoxAutoScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ListBoxAutoScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler",
        typeof(AutoScrollHandler),
        typeof(ListBoxAutoScrollBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WpfListBox listBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            listBox.Loaded += ListBox_Loaded;
            Hook(listBox);
        }
        else
        {
            listBox.Loaded -= ListBox_Loaded;
            Unhook(listBox);
        }
    }

    private static void ListBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfListBox listBox && GetIsEnabled(listBox))
        {
            Hook(listBox);
            ScrollToEnd(listBox);
        }
    }

    private static void Hook(WpfListBox listBox)
    {
        Unhook(listBox);

        if (listBox.ItemsSource is not INotifyCollectionChanged collection)
        {
            return;
        }

        var handler = new AutoScrollHandler(listBox, collection);
        collection.CollectionChanged += handler.OnCollectionChanged;
        listBox.SetValue(HandlerProperty, handler);
    }

    private static void Unhook(WpfListBox listBox)
    {
        if (listBox.GetValue(HandlerProperty) is not AutoScrollHandler handler)
        {
            return;
        }

        handler.Collection.CollectionChanged -= handler.OnCollectionChanged;
        listBox.ClearValue(HandlerProperty);
    }

    private static void ScrollToEnd(WpfListBox listBox)
    {
        if (listBox.Items.Count == 0)
        {
            return;
        }

        listBox.ScrollIntoView(listBox.Items[^1]);
    }

    private sealed class AutoScrollHandler(WpfListBox listBox, INotifyCollectionChanged collection)
    {
        public INotifyCollectionChanged Collection { get; } = collection;

        public void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset))
            {
                return;
            }

            listBox.Dispatcher.BeginInvoke(
                () => ScrollToEnd(listBox),
                DispatcherPriority.Background);
        }
    }
}
