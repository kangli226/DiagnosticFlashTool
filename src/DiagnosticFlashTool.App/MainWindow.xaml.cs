using System.Windows;
using DiagnosticFlashTool.App.ViewModels;

namespace DiagnosticFlashTool.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
