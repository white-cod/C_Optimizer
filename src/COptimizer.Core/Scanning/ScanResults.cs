using COptimizer.Core.Model;

namespace COptimizer.Core.Scanning;

/// <summary>A file found during scanning. Length and timestamp are re-checked before deletion.</summary>
public sealed record FoundFile(string Path, long Length, DateTime LastWriteTimeUtc);

/// <summary>Files found in one concrete folder of a rule target.</summary>
public sealed record TargetScanResult
{
    /// <summary>Concrete normalized folder the files belong to. Never deleted itself.</summary>
    public required string Root { get; init; }

    public required RuleTarget Target { get; init; }

    /// <summary>Owner profile for per-user rules, null for machine rules.</summary>
    public string? UserName { get; init; }

    public required IReadOnlyList<FoundFile> Files { get; init; }

    public long TotalBytes => Files.Sum(f => f.Length);
}

/// <summary>Everything a rule found. This is the "plan" shown to the user and passed to the clean engine.</summary>
public sealed record RuleScanResult
{
    public required RuleDefinition Rule { get; init; }

    public required IReadOnlyList<TargetScanResult> Targets { get; init; }

    /// <summary>Non-fatal problems: denied folders, access errors and so on.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Processes from <see cref="RuleDefinition.CloseProcesses"/> that are currently running.</summary>
    public IReadOnlyList<string> RunningProcesses { get; init; } = [];

    public long TotalBytes => Targets.Sum(t => t.TotalBytes);

    public int FileCount => Targets.Sum(t => t.Files.Count);

    /// <summary>True when the rule's application or data exists on this machine.</summary>
    public bool IsApplicable => Targets.Count > 0;
}

public readonly record struct ScanProgress(int CompletedRules, int TotalRules, string CurrentRuleName);
