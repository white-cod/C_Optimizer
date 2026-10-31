using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace COptimizer.App.Services;

/// <summary>UI-side interactions: dialogs, Explorer, elevation.</summary>
public interface IShellService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    Task ShowErrorAsync(string title, string message);

    void OpenInExplorer(string path);

    /// <summary>Restarts the app elevated. Returns false when the user declined the UAC prompt.</summary>
    bool RestartAsAdministrator();
}

public sealed class ShellService : IShellService
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            Owner = Application.Current.MainWindow,
            MaxWidth = 560,
        };

        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            Owner = Application.Current.MainWindow,
            MaxWidth = 560,
        };

        await box.ShowDialogAsync();
    }

    public void OpenInExplorer(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public bool RestartAsAdministrator()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception)
        {
            return false; // UAC prompt was cancelled.
        }

        Application.Current.Shutdown();
        return true;
    }
}
