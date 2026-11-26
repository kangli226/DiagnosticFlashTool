using DiagnosticFlashTool.App.Views.Controls;
using DiagnosticFlashTool.Core.Configuration;

namespace DiagnosticFlashTool.App.ViewModels;

public sealed class ShellNavigationItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isVisible = true;

    public ShellNavigationItemViewModel(
        string title, FluentIconKind icon, int pageIndex, ProjectConfigEntry? project,
        Action navigate, Func<bool> canNavigate)
    {
        Title = title;
        Icon = icon;
        PageIndex = pageIndex;
        Project = project;
        NavigateCommand = new RelayCommand(navigate, canNavigate);
        NavigateCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(CanNavigate));
    }

    public string Title { get; }
    public FluentIconKind Icon { get; }
    public int PageIndex { get; }
    public ProjectConfigEntry? Project { get; }
    public RelayCommand NavigateCommand { get; }
    public bool CanNavigate => NavigateCommand.CanExecute(null);

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            _isSelected = value;
            // 导航被忙碌状态或未保存提示拦截时，也要恢复 RadioButton 的选择状态。
            OnPropertyChanged();
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }
}

public sealed class ProjectNavigationGroupViewModel : ObservableObject
{
    private bool _isExpanded;

    public ProjectNavigationGroupViewModel(
        ProjectConfigEntry project, ShellNavigationItemViewModel firmwareFlashPage, ShellNavigationItemViewModel flashFlowPage)
    {
        Project = project;
        Pages = [firmwareFlashPage, flashFlowPage];
        OpenCommand = firmwareFlashPage.NavigateCommand;
        foreach (var page in Pages)
        {
            page.IsVisible = false;
        }
    }

    public ProjectConfigEntry Project { get; }
    public IReadOnlyList<ShellNavigationItemViewModel> Pages { get; }
    public RelayCommand OpenCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                foreach (var page in Pages)
                {
                    page.IsVisible = value;
                }
            }
        }
    }
}
