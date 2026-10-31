using COptimizer.Core.Environment;
using COptimizer.Core.Model;
using COptimizer.Core.Safety;
using COptimizer.Core.Scanning;

namespace COptimizer.Core.Cleaning;

/// <summary>
/// Deletes exactly the files from a scan plan. Each file is re-validated right before deletion; anything
/// suspicious is skipped and reported instead of being deleted.
/// </summary>
public sealed class CleanEngine
{
    private const int ProgressInterval = 200;

    private readonly SafetyPolicy _policy;
    private readonly IRecycleBin? _recycleBin;
    private readonly TimeProvider _time;
    private readonly IServiceManager? _services;

    public CleanEngine(CleanupContext context, IRecycleBin? recycleBin, TimeProvider? time = null, IServiceManager? services = null)
    {
        _policy = context.Policy;
        _recycleBin = recycleBin;
        _time = time ?? TimeProvider.System;
        _services = services;
    }

    public Task<CleanReport> CleanAsync(
        IReadOnlyList<RuleScanResult> plan,
        CleanOptions options,
        IProgress<CleanProgress>? progress = null,
        ICleanLog? log = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Clean(plan, options, progress, log, cancellationToken), cancellationToken);

    public CleanReport Clean(
        IReadOnlyList<RuleScanResult> plan,
        CleanOptions options,
        IProgress<CleanProgress>? progress = null,
        ICleanLog? log = null,
        CancellationToken cancellationToken = default)
    {
        var report = new CleanReport { DryRun = options.DryRun, StartedAt = _time.GetUtcNow() };
        var totalFiles = plan.Sum(r => (long)r.FileCount);
        long processed = 0;
        var fileLog = options.DryRun ? null : log;

        foreach (var ruleResult in plan)
        {
            var rule = ruleResult.Rule;
            if (rule.DeleteMode == DeleteMode.RecycleBin && _recycleBin is null && !options.DryRun)
                throw new InvalidOperationException($"Rule '{rule.Id}' requires a Recycle Bin implementation.");

            var ruleReport = new RuleCleanReport { RuleId = rule.Id, RuleName = rule.Name };
            report.Rules.Add(ruleReport);

            // Services that hold the files (e.g. Windows Update) are stopped for the rule and restarted afterwards.
            using var stopped = rule.StopServices.Count > 0 && !options.DryRun && ruleResult.FileCount > 0
                ? _services?.StopTemporarily(rule.StopServices, cancellationToken)
                : null;
            if (stopped is not null)
                ruleReport.Notes.AddRange(stopped.Problems);
            else if (rule.StopServices.Count > 0 && _services is null && !options.DryRun)
                ruleReport.Notes.Add("Services were not stopped (no service manager); files in use are skipped.");

            foreach (var target in ruleResult.Targets)
            {
                var verifiedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in target.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var skipped = ProcessFile(file, target.Root, rule.DeleteMode, options.DryRun, verifiedDirs);
                    if (skipped is null)
                    {
                        ruleReport.RemovedFiles++;
                        ruleReport.RemovedBytes += file.Length;
                        fileLog?.Removed(rule.Id, file.Path, file.Length);
                    }
                    else
                    {
                        ruleReport.Skipped.Add(skipped);
                        fileLog?.Skipped(rule.Id, skipped);
                    }

                    if (++processed % ProgressInterval == 0)
                        progress?.Report(new CleanProgress(processed, totalFiles, report.RemovedBytes, file.Path));
                }

                if (target.Target.RemoveEmptyFolders && !options.DryRun)
                    ruleReport.RemovedFolders += RemoveEmptySubfolders(target.Root);
            }
        }

        progress?.Report(new CleanProgress(processed, totalFiles, report.RemovedBytes, string.Empty));
        report.FinishedAt = _time.GetUtcNow();
        return report;
    }

    /// <summary>Removes (or, in a dry run, validates) one file. Returns null on success, otherwise why it was skipped.</summary>
    private SkippedFile? ProcessFile(FoundFile file, string root, DeleteMode mode, bool dryRun, HashSet<string> verifiedDirs)
    {
        var check = _policy.CheckItem(file.Path, root);
        if (!check.IsAllowed)
            return new SkippedFile(file.Path, file.Length, SkipReason.SafetyCheckFailed, check.Reason);

        if (!ParentChainIsReal(check.NormalizedPath, root, verifiedDirs))
            return new SkippedFile(file.Path, file.Length, SkipReason.SafetyCheckFailed, "A folder on the way is a junction or symbolic link.");

        FileInfo info;
        try
        {
            info = new FileInfo(check.NormalizedPath);
            if (!info.Exists)
                return new SkippedFile(file.Path, file.Length, SkipReason.Missing);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SkippedFile(file.Path, file.Length, SkipReason.InUseOrAccessDenied, e.Message);
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return new SkippedFile(file.Path, file.Length, SkipReason.SafetyCheckFailed, "File is a reparse point.");

        if (info.Length != file.Length || info.LastWriteTimeUtc != file.LastWriteTimeUtc)
            return new SkippedFile(file.Path, file.Length, SkipReason.ChangedSinceScan);

        if (dryRun)
            return null;

        try
        {
            if (mode == DeleteMode.RecycleBin)
            {
                _recycleBin!.MoveToRecycleBin(info.FullName);
            }
            else
            {
                if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                    info.Attributes &= ~FileAttributes.ReadOnly;

                info.Delete();
            }

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SkippedFile(file.Path, file.Length, SkipReason.InUseOrAccessDenied, e.Message);
        }
    }

    /// <summary>Verifies that no folder between the target root and the file became a reparse point after the scan.</summary>
    private static bool ParentChainIsReal(string filePath, string root, HashSet<string> verifiedDirs)
    {
        var dir = Path.GetDirectoryName(filePath);
        var toVerify = new List<string>();

        while (dir is not null && PathUtil.IsSameOrInside(dir, root) && !verifiedDirs.Contains(dir))
        {
            toVerify.Add(dir);
            if (dir.Equals(root, StringComparison.OrdinalIgnoreCase))
                break;
            dir = Path.GetDirectoryName(dir);
        }

        foreach (var d in toVerify)
        {
            if (!PathPatternExpander.IsRealDirectory(d))
                return false;
        }

        verifiedDirs.UnionWith(toVerify);
        return true;
    }

    /// <summary>Removes empty subfolders (bottom-up). The root itself is kept. Returns the number removed.</summary>
    private int RemoveEmptySubfolders(string root)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };

        int Visit(string dir)
        {
            string[] children;
            try
            {
                children = new DirectoryInfo(dir).EnumerateDirectories("*", options)
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    .Select(d => d.FullName)
                    .ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return 0;
            }

            var count = 0;
            foreach (var child in children)
            {
                count += Visit(child);
                if (!_policy.CheckItem(child, root).IsAllowed)
                    continue;

                try
                {
                    if (!Directory.EnumerateFileSystemEntries(child).Any())
                    {
                        Directory.Delete(child, recursive: false);
                        count++;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Folder is in use or not empty — keep it.
                }
            }

            return count;
        }

        return Visit(root);
    }
}
