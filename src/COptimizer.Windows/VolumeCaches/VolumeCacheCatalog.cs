using COptimizer.Core.Model;
using COptimizer.Core.SystemCleaners;

namespace COptimizer.Windows.VolumeCaches;

/// <summary>
/// Allow-list of Disk Cleanup handlers that C_Optimizer runs, with its own names, descriptions and risk levels.
/// Handlers not listed here are never used: some of them delete user data (e.g. "DownloadsFolder" empties the
/// Downloads folder, "User file versions" deletes File History), and others duplicate built-in rules.
/// </summary>
public static class VolumeCacheCatalog
{
    private sealed record Entry(string KeyName, CleanerInfo Info, bool IsSlow = false);

    private static readonly Entry[] Entries =
    [
        new("Previous Installations", new CleanerInfo
        {
            Id = "windows.previous-installations",
            Name = "Previous Windows installation (Windows.old)",
            Description = "The old copy of Windows kept after a feature update. After removing it you can no longer go back to the previous version of Windows.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Moderate,
        }),
        new("Update Cleanup", new CleanerInfo
        {
            Id = "windows.update-cleanup",
            Name = "Windows Update cleanup (component store)",
            Description = "Superseded versions of system components in WinSxS. Old updates can no longer be uninstalled. Cleaning can take several minutes.",
            Category = CleanerCategory.WindowsUpdate,
            Risk = RiskLevel.Safe,
        }, IsSlow: true),
        new("Delivery Optimization Files", new CleanerInfo
        {
            Id = "windows.delivery-optimization",
            Name = "Delivery Optimization files",
            Description = "Update files cached to share with other PCs. Windows downloads them again if needed.",
            Category = CleanerCategory.WindowsUpdate,
            Risk = RiskLevel.Safe,
        }),
        new("Temporary Setup Files", new CleanerInfo
        {
            Id = "windows.temporary-setup-files",
            Name = "Temporary Windows Setup files",
            Description = "Files left behind by Windows Setup and feature updates.",
            Category = CleanerCategory.WindowsUpdate,
            Risk = RiskLevel.Safe,
        }),
        new("Upgrade Discarded Files", new CleanerInfo
        {
            Id = "windows.upgrade-discarded-files",
            Name = "Files discarded by Windows upgrade",
            Description = "Files that Windows Setup moved aside during an upgrade and no longer needs.",
            Category = CleanerCategory.WindowsUpdate,
            Risk = RiskLevel.Safe,
        }),
        new("Windows Upgrade Log Files", new CleanerInfo
        {
            Id = "windows.upgrade-logs",
            Name = "Windows upgrade log files",
            Description = "Logs written during Windows upgrades. Only needed to troubleshoot a failed upgrade.",
            Category = CleanerCategory.WindowsUpdate,
            Risk = RiskLevel.Safe,
        }),
        new("Windows Reset Log Files", new CleanerInfo
        {
            Id = "windows.reset-logs",
            Name = "Windows reset log files",
            Description = "Logs written by 'Reset this PC'.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
        new("Setup Log Files", new CleanerInfo
        {
            Id = "windows.setup-logs",
            Name = "Setup log files",
            Description = "Log files created by Windows and application setup.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
        new("Windows ESD installation files", new CleanerInfo
        {
            Id = "windows.esd-files",
            Name = "Windows ESD installation files",
            Description = "Installation image used by 'Reset this PC'. After removing it, a reset downloads Windows from the internet instead.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Moderate,
        }),
        new("System error memory dump files", new CleanerInfo
        {
            Id = "windows.memory-dump",
            Name = "System error memory dump (MEMORY.DMP)",
            Description = "Full memory dump written after a blue screen. Only needed to diagnose that crash.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
        new("Device Driver Packages", new CleanerInfo
        {
            Id = "windows.old-driver-packages",
            Name = "Old device driver packages",
            Description = "Previous versions of installed drivers. Rolling a driver back to an older version will no longer be possible.",
            Category = CleanerCategory.Gpu,
            Risk = RiskLevel.Moderate,
        }),
        new("Language Pack", new CleanerInfo
        {
            Id = "windows.unused-language-resources",
            Name = "Unused language resources",
            Description = "Language packs and features that are not used. They are downloaded again if you add the language later.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Moderate,
        }),
        new("Windows Defender", new CleanerInfo
        {
            Id = "windows.defender-temp",
            Name = "Microsoft Defender temporary files",
            Description = "Non-critical files used by Microsoft Defender Antivirus (support logs and local copies).",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
        new("Diagnostic Data Viewer database files", new CleanerInfo
        {
            Id = "windows.diagnostic-data-viewer",
            Name = "Diagnostic Data Viewer database",
            Description = "Local copy of diagnostic data shown in the Diagnostic Data Viewer.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
        new("Feedback Hub Archive log files", new CleanerInfo
        {
            Id = "windows.feedback-hub-archive",
            Name = "Feedback Hub archived logs",
            Description = "Diagnostic logs collected for Feedback Hub reports.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
        new("RetailDemo Offline Content", new CleanerInfo
        {
            Id = "windows.retail-demo",
            Name = "Retail demo content",
            Description = "Content for the store demo mode of Windows. Not used on personal PCs.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
        new("Old ChkDsk Files", new CleanerInfo
        {
            Id = "windows.old-chkdsk-files",
            Name = "Old Check Disk fragments",
            Description = "File fragments recovered by Check Disk (FOUND.000 folders).",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
        new("BranchCache", new CleanerInfo
        {
            Id = "windows.branchcache",
            Name = "BranchCache",
            Description = "Content cached by BranchCache for other computers on the network.",
            Category = CleanerCategory.System,
            Risk = RiskLevel.Safe,
        }),
    ];

    /// <summary>Handler key names that are deliberately never used.</summary>
    public static IReadOnlySet<string> Excluded { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "DownloadsFolder",          // user data: empties Downloads
        "User file versions",       // user data: File History versions
        "Recycle Bin",              // own RecycleBinCleaner (per-drive details, Moderate risk)
        "Temporary Files",          // covered by rules with age limits and per-profile details
        "Thumbnail Cache",          // covered by rules
        "Internet Cache Files",     // covered by rules
        "D3D Shader Cache",         // covered by rules
        "Windows Error Reporting Files", // covered by rules
        "System error minidump files",   // covered by rules
        "Offline Pages Files",
        "Downloaded Program Files",
    };

    /// <summary>Creates cleaners for allow-listed handlers that are registered on this machine.</summary>
    public static IReadOnlyList<VolumeCacheCleaner> CreateAvailable(string systemVolume) =>
        Entries
            .Where(e => VolumeCacheCleaner.IsRegistered(e.KeyName))
            .Select(e => new VolumeCacheCleaner(e.KeyName, e.Info, e.IsSlow, systemVolume))
            .ToArray();

    internal static IEnumerable<string> AllowedKeyNames => Entries.Select(e => e.KeyName);
}
