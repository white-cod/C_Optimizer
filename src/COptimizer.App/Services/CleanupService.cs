using COptimizer.Core;
using COptimizer.Core.Cleaning;
using COptimizer.Core.Model;
using COptimizer.Core.Scanning;
using COptimizer.Core.SystemCleaners;
using COptimizer.Rules;
using COptimizer.Windows;

namespace COptimizer.App.Services;

public sealed record DiskUsage(string DriveName, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    public double UsedPercent => TotalBytes > 0 ? UsedBytes * 100.0 / TotalBytes : 0;
}

/// <summary>A system cleaner selected for cleaning, with the size estimated by the last scan.</summary>
public sealed record SystemCleanerSelection(ISystemCleaner Cleaner, long EstimatedBytes);

/// <summary>What to clean: fresh rule scan results and selected system cleaners.</summary>
public sealed record CleanPlan(IReadOnlyList<RuleScanResult> Rules, IReadOnlyList<SystemCleanerSelection> SystemCleaners)
{
    public long EstimatedBytes => Rules.Sum(r => r.TotalBytes) + SystemCleaners.Sum(s => s.EstimatedBytes);

    public bool IsEmpty => Rules.Count == 0 && SystemCleaners.Count == 0;
}

public readonly record struct CleanStatus(double Percent, string Text);

public sealed record SystemCleanOutcome(CleanerInfo Info, SystemCleanResult Result);

public sealed record CleanOutcome(CleanReport Report, IReadOnlyList<SystemCleanOutcome> SystemResults, string? LogPath)
{
    public long FreedBytes => Report.RemovedBytes + SystemResults.Sum(s => s.Result.FreedBytes);

    public IEnumerable<string> Problems =>
        Report.Rules.SelectMany(r => r.Notes)
            .Concat(SystemResults.Where(s => !s.Result.Succeeded).Select(s => $"{s.Info.Name}: {s.Result.Error}"));
}

/// <summary>Everything the UI needs from the engine. Abstracted so view models can be tested with fakes.</summary>
public interface ICleanupService
{
    bool IsAdministrator { get; }

    IReadOnlyList<RuleDefinition> Rules { get; }

    /// <summary>System cleaners usable with the current rights.</summary>
    IReadOnlyList<ISystemCleaner> SystemCleaners { get; }

    DiskUsage GetSystemDriveUsage();

    Task<IReadOnlyList<RuleScanResult>> ScanAsync(
        IReadOnlyList<RuleDefinition> rules, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);

    Task<CleanOutcome> CleanAsync(CleanPlan plan, IProgress<CleanStatus>? progress, CancellationToken cancellationToken);
}

public sealed class CleanupService : ICleanupService
{
    private readonly Lazy<IReadOnlyList<ISystemCleaner>> _systemCleaners;

    public CleanupService()
    {
        _systemCleaners = new(() => WindowsSystemCleaners.Create(SystemDrive)
            .Where(c => IsAdministrator || !c.RequiresAdministrator)
            .ToArray());
    }

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "COptimizer");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "Logs");

    private static string SystemDrive => Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

    public bool IsAdministrator { get; } = Elevation.IsAdministrator();

    public IReadOnlyList<RuleDefinition> Rules => BuiltInRules.All;

    public IReadOnlyList<ISystemCleaner> SystemCleaners => _systemCleaners.Value;

    public DiskUsage GetSystemDriveUsage()
    {
        var drive = new DriveInfo(SystemDrive);
        return new DiskUsage(SystemDrive.TrimEnd('\\'), drive.TotalSize, drive.AvailableFreeSpace);
    }

    public Task<IReadOnlyList<RuleScanResult>> ScanAsync(
        IReadOnlyList<RuleDefinition> rules, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        // Profiles and paths are re-read on every scan: cheap, and always reflects the current machine state.
        var scanner = new Scanner(CreateContext(), systemState: new WindowsSystemState());
        return Task.Run(() => scanner.ScanAsync(rules, progress, cancellationToken), cancellationToken);
    }

    public async Task<CleanOutcome> CleanAsync(CleanPlan plan, IProgress<CleanStatus>? progress, CancellationToken cancellationToken)
    {
        using var log = FileCleanLog.CreateIn(LogsDirectory);
        double total = Math.Max(1, plan.EstimatedBytes);
        double done = 0;

        // 1. Files from rules.
        var engine = new CleanEngine(CreateContext(), new ShellRecycleBin(), services: new WindowsServiceManager());
        var fileProgress = progress is null ? null : new Progress<CleanProgress>(p =>
            progress.Report(new CleanStatus(p.RemovedBytes * 100 / total, $"Removing files… {ByteSize.Format(p.RemovedBytes)} freed")));
        var report = await engine.CleanAsync(plan.Rules, new CleanOptions(), fileProgress, log, cancellationToken).ConfigureAwait(false);
        foreach (var note in report.Rules.SelectMany(r => r.Notes.Select(n => $"{r.RuleId}: {n}")))
            log.Note(note);
        done += plan.Rules.Sum(r => r.TotalBytes);

        // 2. System cleaners, one after another (Windows cleanup handlers must not run concurrently).
        var systemResults = new List<SystemCleanOutcome>();
        foreach (var selection in plan.SystemCleaners)
        {
            var info = selection.Cleaner.Info;
            var baseline = done;
            progress?.Report(new CleanStatus(baseline * 100 / total, $"{info.Name}…"));
            var cleanerProgress = progress is null ? null : new Progress<SystemCleanProgress>(p =>
                progress.Report(new CleanStatus(
                    Math.Min(100, (baseline + Math.Min(p.FreedBytes, selection.EstimatedBytes)) * 100 / total),
                    $"{info.Name}… {ByteSize.Format(p.FreedBytes)} of {ByteSize.Format(Math.Max(p.TotalBytes, selection.EstimatedBytes))}")));

            SystemCleanResult result;
            try
            {
                result = await selection.Cleaner.CleanAsync(cleanerProgress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or System.Runtime.InteropServices.COMException)
            {
                result = SystemCleanResult.Failure(e.Message);
            }

            log.Note(result.Succeeded
                ? $"{info.Id}: freed {result.FreedBytes} bytes"
                : $"{info.Id}: failed: {result.Error}");
            systemResults.Add(new SystemCleanOutcome(info, result));
            done += selection.EstimatedBytes;
        }

        return new CleanOutcome(report, systemResults, log.Path);
    }

    private static CleanupContext CreateContext() =>
        CleanupContext.Create(new WindowsSystemPathsProvider(), new RegistryProfileProvider());
}
