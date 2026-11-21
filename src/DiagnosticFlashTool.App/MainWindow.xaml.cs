using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DiagnosticFlashTool.App.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace DiagnosticFlashTool.App;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Point? _newFlowProjectDialogDragStart;
    private Vector _newFlowProjectDialogStartOffset;
    private IInputElement? _newFlowProjectDialogPreviousFocus;
    private bool _exitRequested;
    private readonly double _logPanelMinimumHeight;
    private double _logPanelHeight;

    public MainWindow() : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _logPanelMinimumHeight = LogPanelRow.MinHeight;
        _logPanelHeight = LogPanelRow.Height.Value;
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

            // The CAN device has to be released before the process exits, otherwise the
            // native driver handle stays open. DisposeAsync is UI-free and every device
            // implementation completes synchronously, so waiting here cannot deadlock.
            // The wait is still bounded so a misbehaving driver can never hang the exit.
            try
            {
                if (!viewModel.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)))
                {
                    Debug.WriteLine("Timed out while releasing the CAN device.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to release the CAN device: {ex}");
            }
        }

        _notifyIcon?.Dispose();
        _notifyIcon = null;
        base.OnClosed(e);
    }

    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsOutputPanelVisible)
            && sender is MainViewModel logViewModel)
        {
            // GridSplitter 会直接设置行高；在视图层收起并恢复行高，保留用户拖拽后的尺寸。
            if (logViewModel.IsOutputPanelVisible)
            {
                LogPanelRow.MinHeight = _logPanelMinimumHeight;
                SetLogPanelHeight(_logPanelHeight);
            }
            else
            {
                _logPanelHeight = Math.Max(_logPanelMinimumHeight, LogPanelRow.ActualHeight);
                LogPanelRow.MinHeight = 0;
                LogPanelRow.Height = new GridLength(0);
            }
        }

        if (e.PropertyName == nameof(MainViewModel.KeepRunningInTray)
            && sender is MainViewModel viewModel)
        {
            SyncNotifyIcon(viewModel);
        }
    }

    private void LogPanelHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel { IsOutputPanelVisible: true })
        {
            SetLogPanelHeight(LogPanelRow.Height.Value);
        }
    }

    private void SetLogPanelHeight(double height)
    {
        // 缩小窗口或重新展开时，为业务页面保留最小高度，避免固定日志行把状态栏挤出窗口。
        var maximumHeight = Math.Max(
            _logPanelMinimumHeight,
            LogPanelHost.ActualHeight - MainContentRow.MinHeight - LogPanelSplitter.Height);
        LogPanelRow.Height = new GridLength(Math.Clamp(height, _logPanelMinimumHeight, maximumHeight));
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

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || sender is not System.Windows.Controls.MenuItem menuItem
            || menuItem.Tag is null
            || !int.TryParse(menuItem.Tag.ToString(), out var shellIndex))
        {
            return;
        }

        viewModel.NavigateToUdsPage(shellIndex);
    }

    private void NavigateProject_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || sender is not System.Windows.Controls.MenuItem menuItem
            || !int.TryParse(menuItem.Tag?.ToString(), out var shellIndex))
        {
            return;
        }

        viewModel.NavigateToProjectPage(viewModel.SelectedProject, shellIndex);
    }

    private void ShellNavigationPage_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: ShellNavigationItemViewModel { IsSelected: false } page })
        {
            // Checked 同时覆盖鼠标、方向键和辅助功能触发；业务选择由同一个命令处理。
            page.NavigateCommand.Execute(null);
        }
    }

    private void FlowStepTemplateDialogOverlay_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => FlowStepTemplateComboBox.Focus()));
        }
    }

    private void NewFlowProjectDialogOverlay_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            EndNewFlowProjectDialogDrag(NewFlowProjectDialogHeader);
            var previousFocus = _newFlowProjectDialogPreviousFocus;
            _newFlowProjectDialogPreviousFocus = null;
            if (previousFocus is not null)
            {
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    new Action(() =>
                    {
                        if (previousFocus is UIElement { IsVisible: true, IsEnabled: true } element)
                        {
                            element.Focus();
                        }
                    }));
            }

            return;
        }

        _newFlowProjectDialogPreviousFocus = Keyboard.FocusedElement;
        NewFlowProjectDialogTransform.X = 0;
        NewFlowProjectDialogTransform.Y = 0;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => NewFlowProjectNameTextBox.Focus()));
    }

    private void NewFlowProjectDialogHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not UIElement header)
        {
            return;
        }

        _newFlowProjectDialogDragStart = e.GetPosition(NewFlowProjectDialogOverlay);
        _newFlowProjectDialogStartOffset = new Vector(
            NewFlowProjectDialogTransform.X,
            NewFlowProjectDialogTransform.Y);
        header.CaptureMouse();
        e.Handled = true;
    }

    private void NewFlowProjectDialogHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (_newFlowProjectDialogDragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            EndNewFlowProjectDialogDrag(sender);
            return;
        }

        var currentPosition = e.GetPosition(NewFlowProjectDialogOverlay);
        var horizontalLimit = Math.Max(
            0,
            (NewFlowProjectDialogOverlay.ActualWidth - NewFlowProjectDialogPanel.ActualWidth) / 2 - 16);
        var verticalLimit = Math.Max(
            0,
            (NewFlowProjectDialogOverlay.ActualHeight - NewFlowProjectDialogPanel.ActualHeight) / 2 - 16);

        NewFlowProjectDialogTransform.X = Math.Clamp(
            _newFlowProjectDialogStartOffset.X + currentPosition.X - _newFlowProjectDialogDragStart.Value.X,
            -horizontalLimit,
            horizontalLimit);
        NewFlowProjectDialogTransform.Y = Math.Clamp(
            _newFlowProjectDialogStartOffset.Y + currentPosition.Y - _newFlowProjectDialogDragStart.Value.Y,
            -verticalLimit,
            verticalLimit);
    }

    private void NewFlowProjectDialogHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndNewFlowProjectDialogDrag(sender);
        e.Handled = true;
    }

    private void EndNewFlowProjectDialogDrag(object sender)
    {
        _newFlowProjectDialogDragStart = null;
        if (sender is UIElement header && header.IsMouseCaptured)
        {
            header.ReleaseMouseCapture();
        }
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
