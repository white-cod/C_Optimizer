using COptimizer.App.ViewModels;
using Wpf.Ui.Appearance;

namespace COptimizer.App;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        ApplicationThemeManager.ApplySystemTheme();
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            SystemThemeWatcher.Watch(this);
            await _viewModel.InitializeAsync();
        };
    }
}
