using System.ComponentModel;
using System.Windows;
using DiagnosticFlashTool.Product.EcuX.ViewModels;

namespace DiagnosticFlashTool.Product.EcuX;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new ProductViewModel();
        viewModel.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
        DataContext = viewModel;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is ProductViewModel viewModel)
        {
            viewModel.LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
            viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private void LogEntries_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (LogList.Items.Count > 0)
        {
            LogList.ScrollIntoView(LogList.Items[^1]);
        }
    }
}
