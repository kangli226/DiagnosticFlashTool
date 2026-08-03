using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiagnosticFlashTool.App.ViewModels;

namespace DiagnosticFlashTool.App.Views;

public partial class SystemSettingsView : UserControl
{
    public SystemSettingsView()
    {
        InitializeComponent();
    }

    private void AdminModeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { AdminModeEnabled: true })
        {
            return;
        }

        ShowAdminPasswordOverlay();
    }

    private void SubmitAdminPassword_Click(object sender, RoutedEventArgs e)
    {
        SubmitAdminPassword();
    }

    private void CancelAdminPassword_Click(object sender, RoutedEventArgs e)
    {
        HideAdminPasswordOverlay();
    }

    private void AdminPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitAdminPassword();
        }

        if (e.Key == Key.Escape)
        {
            HideAdminPasswordOverlay();
        }
    }

    private void SubmitAdminPassword()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.EnableAdminMode(AdminPasswordBox.Password))
        {
            HideAdminPasswordOverlay();
            return;
        }

        AdminPasswordBox.Clear();
        AdminPasswordBox.Focus();
        MessageBox.Show("密码错误。", "管理员模式", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowAdminPasswordOverlay()
    {
        AdminPasswordBox.Clear();
        AdminPasswordOverlay.Visibility = Visibility.Visible;
        AdminPasswordBox.Focus();
    }

    private void HideAdminPasswordOverlay()
    {
        AdminPasswordBox.Clear();
        AdminPasswordOverlay.Visibility = Visibility.Collapsed;
    }
}
