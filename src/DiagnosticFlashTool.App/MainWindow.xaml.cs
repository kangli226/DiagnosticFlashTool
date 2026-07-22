using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DiagnosticFlashTool.App.ViewModels;

namespace DiagnosticFlashTool.App;

public partial class MainWindow : Window
{
    private const int DwmWindowCornerPreferenceAttribute = 33;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyNativeRoundedCorners();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShellNav_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || sender is not RadioButton radioButton
            || radioButton.CommandParameter is null
            || !int.TryParse(radioButton.CommandParameter.ToString(), out var shellIndex))
        {
            return;
        }

        viewModel.SelectedShellIndex = shellIndex;
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ApplyNativeRoundedCorners()
    {
        if (Environment.OSVersion.Version.Major < 10)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var preference = (int)DwmWindowCornerPreference.Round;
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmWindowCornerPreferenceAttribute,
            ref preference,
            Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }
}
