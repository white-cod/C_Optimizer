namespace COptimizer.Core.Cleaning;

public interface IRecycleBin
{
    /// <summary>Moves a file to the Recycle Bin silently. Throws <see cref="IOException"/> on failure.</summary>
    void MoveToRecycleBin(string path);
}

public sealed record CleanOptions
{
    /// <summary>Report what would be removed without touching anything.</summary>
    public bool DryRun { get; init; }
}

public enum SkipReason
{
    /// <summary>File was already removed (by the app itself, another rule, or the user).</summary>
    Missing,

    /// <summary>File is open by another process or access is denied.</summary>
    InUseOrAccessDenied,

    /// <summary>File size or timestamp changed after scanning.</summary>
    ChangedSinceScan,

    /// <summary>Path failed a safety check (protected area, reparse point on the way, outside its target).</summary>
    SafetyCheckFailed,
}

public sealed record SkippedFile(string Path, long Length, SkipReason Reason, string? Details = null);

public sealed class RuleCleanReport
{
    public required string RuleId { get; init; }

    public required string RuleName { get; init; }

    public int RemovedFiles { get; internal set; }

    public long RemovedBytes { get; internal set; }

    public int RemovedFolders { get; internal set; }

    public List<SkippedFile> Skipped { get; } = [];

    /// <summary>Non-file problems, e.g. a service that could not be stopped.</summary>
    public List<string> Notes { get; } = [];

    public long SkippedBytes => Skipped.Where(s => s.Reason != SkipReason.Missing).Sum(s => s.Length);
}

public sealed class CleanReport
{
    public required bool DryRun { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset FinishedAt { get; internal set; }

    public List<RuleCleanReport> Rules { get; } = [];

    public long RemovedBytes => Rules.Sum(r => r.RemovedBytes);

    public int RemovedFiles => Rules.Sum(r => r.RemovedFiles);

    public int SkippedFiles => Rules.Sum(r => r.Skipped.Count(s => s.Reason != SkipReason.Missing));
}

public readonly record struct CleanProgress(long ProcessedFiles, long TotalFiles, long RemovedBytes, string CurrentPath);
