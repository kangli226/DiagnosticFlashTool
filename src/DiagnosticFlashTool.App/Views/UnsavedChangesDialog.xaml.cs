using System.ComponentModel;
using System.Windows;

namespace DiagnosticFlashTool.App.Views;

internal enum UnsavedChangesChoice
{
    Cancel,
    Save,
    Discard
}

public sealed partial class UnsavedChangesDialog : Window
{
    internal UnsavedChangesChoice Choice { get; private set; } = UnsavedChangesChoice.Cancel;

    public UnsavedChangesDialog(string configName, string action)
    {
        InitializeComponent();
        PromptTextBlock.Text = $"配置“{configName}”存在未保存的修改。{action}前是否保存？";

        if (Application.Current?.MainWindow is { IsVisible: true } mainWindow)
        {
            Owner = mainWindow;
        }

        ContentRendered += (_, _) => SaveButton.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Choice = UnsavedChangesChoice.Save;
        DialogResult = true;
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        Choice = UnsavedChangesChoice.Discard;
        DialogResult = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Choice = UnsavedChangesChoice.Cancel;
        Close();
    }

    // 标题栏关闭、Alt+F4 等所有关闭路径都必须落到“取消”，不得把默认值之外的结果交给宿主。
    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult is null)
        {
            Choice = UnsavedChangesChoice.Cancel;
        }

        base.OnClosing(e);
    }
}
