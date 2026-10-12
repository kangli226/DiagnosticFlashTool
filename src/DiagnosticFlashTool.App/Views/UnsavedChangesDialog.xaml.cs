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
}
