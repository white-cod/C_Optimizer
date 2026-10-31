using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using COptimizer.App.Services;
using COptimizer.Core;
using Wpf.Ui.Controls;

namespace COptimizer.App.ViewModels;

public sealed record NavigationItem(string Title, SymbolRegular Icon, object Page);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IShellService _shell;

    public MainViewModel(ICleanupService service, IShellService shell)
    {
        _shell = shell;
        IsAdministrator = service.IsAdministrator;
        Cleanup = new CleanupViewModel(service, shell);
        Home = new HomeViewModel(service, Cleanup, ShowCleanupPage);
        Items =
        [
            new NavigationItem("Home", SymbolRegular.Home24, Home),
            new NavigationItem("Cleanup", SymbolRegular.Broom24, Cleanup),
        ];
        SelectedItem = Items[0];
    }

    public HomeViewModel Home { get; }

    public CleanupViewModel Cleanup { get; }

    public IReadOnlyList<NavigationItem> Items { get; }

    [ObservableProperty]
    public partial NavigationItem SelectedItem { get; set; }

    public bool IsAdministrator { get; }

    public bool ShowAdministratorBanner => !IsAdministrator;

    /// <summary>Called when the window is shown: scanning is read-only and fast, so results are ready immediately.</summary>
    public Task InitializeAsync() => Cleanup.ScanCommand.ExecuteAsync(null);

    private void ShowCleanupPage() => SelectedItem = Items[1];

    [RelayCommand]
    private void RestartAsAdministrator() => _shell.RestartAsAdministrator();
}

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly ICleanupService _service;
    private readonly Action _showCleanup;

    public HomeViewModel(ICleanupService service, CleanupViewModel cleanup, Action showCleanup)
    {
        _service = service;
        _showCleanup = showCleanup;
        Cleanup = cleanup;
        Cleanup.CleanFinished += (_, _) => RefreshDisk();
        RefreshDisk();
    }

    public CleanupViewModel Cleanup { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DriveTitle), nameof(FreeText), nameof(UsedPercent), nameof(IsLowOnSpace))]
    public partial DiskUsage? Disk { get; private set; }

    public string DriveTitle => Disk is null ? "System drive" : $"Local Disk ({Disk.DriveName})";

    public string FreeText => Disk is null
        ? string.Empty
        : $"{ByteSize.Format(Disk.FreeBytes)} free of {ByteSize.Format(Disk.TotalBytes)}";

    public double UsedPercent => Disk?.UsedPercent ?? 0;

    /// <summary>Less than 10% free: the usage bar turns red.</summary>
    public bool IsLowOnSpace => Disk is { TotalBytes: > 0 } d && d.FreeBytes * 10 < d.TotalBytes;

    [RelayCommand]
    private void ShowCleanup() => _showCleanup();

    [RelayCommand]
    private void RefreshDisk()
    {
        try
        {
            Disk = _service.GetSystemDriveUsage();
        }
        catch (IOException)
        {
            Disk = null;
        }
    }
}
