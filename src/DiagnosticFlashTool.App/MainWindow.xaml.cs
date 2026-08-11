using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DiagnosticFlashTool.App.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DiagnosticFlashTool.App;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private Forms.NotifyIcon? _notifyIcon;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        viewModel.PropertyChanged += MainViewModel_PropertyChanged;
        DataContext = viewModel;
        SyncNotifyIcon(viewModel);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested
            && DataContext is MainViewModel { KeepRunningInTray: true })
        {
            e.Cancel = true;
            EnsureNotifyIcon();
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged -= MainViewModel_PropertyChanged;
        }

        _notifyIcon?.Dispose();
        _notifyIcon = null;
        base.OnClosed(e);
    }

    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.KeepRunningInTray)
            && sender is MainViewModel viewModel)
        {
            SyncNotifyIcon(viewModel);
        }
    }

    private void SyncNotifyIcon(MainViewModel viewModel)
    {
        if (viewModel.KeepRunningInTray)
        {
            EnsureNotifyIcon();
            return;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }
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

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return nint.Zero;
        }

        handled = true;
        AdjustMaximizedBounds(hwnd, lParam);
        return nint.Zero;
    }

    private static void AdjustMaximizedBounds(nint hwnd, nint lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultTonearest);
        if (monitor == nint.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo
        {
            cbSize = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.ptMaxPosition.x = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
        minMaxInfo.ptMaxPosition.y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
        minMaxInfo.ptMaxSize.x = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
        minMaxInfo.ptMaxSize.y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    private void EnsureNotifyIcon()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = true;
            return;
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "BOOT刷写工具",
            Icon = LoadNotifyIcon(),
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitFromTray));
        _notifyIcon.ContextMenuStrip = menu;
    }

    private static Drawing.Icon LoadNotifyIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "resources", "download.ico");
        return File.Exists(iconPath)
            ? new Drawing.Icon(iconPath)
            : Drawing.SystemIcons.Application;
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }

        System.Windows.Application.Current.Shutdown();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    private const int MonitorDefaultTonearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
