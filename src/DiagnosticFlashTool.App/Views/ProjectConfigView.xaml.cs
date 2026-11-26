using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DiagnosticFlashTool.App.ViewModels;

namespace DiagnosticFlashTool.App.Views;

public partial class ProjectConfigView : UserControl
{
    public ProjectConfigView()
    {
        InitializeComponent();
    }

    private void ProjectsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left
            && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ProjectsGrid, source) is DataGridRow row
            && DataContext is MainViewModel viewModel
            && ReferenceEquals(row.Item, viewModel.SelectedProject)
            && viewModel.EditProjectCommand.CanExecute(null))
        {
            viewModel.EditProjectCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void NewProjectDialog_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (NewProjectDialogPanel.IsVisible)
            {
                NewProjectDialogPanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
            else if (NewProjectButton.IsVisible && NewProjectButton.IsEnabled)
            {
                NewProjectButton.Focus();
            }
        }, DispatcherPriority.Input);
    }
}
