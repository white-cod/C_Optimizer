using System.Windows;
using System.Windows.Threading;
using COptimizer.App.Services;
using COptimizer.App.ViewModels;

namespace COptimizer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var viewModel = new MainViewModel(new CleanupService(), new ShellService());
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(CleanupService.LogsDirectory);
            File.AppendAllText(
                Path.Combine(CleanupService.LogsDirectory, "errors.log"),
                $"{DateTimeOffset.Now:O} {e.Exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never hide the original error.
        }

        MessageBox.Show(e.Exception.Message, "C_Optimizer - unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
