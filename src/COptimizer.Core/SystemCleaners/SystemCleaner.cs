using COptimizer.Core.Model;

namespace COptimizer.Core.SystemCleaners;

/// <summary>Display and policy information shared by all system cleaners.</summary>
public sealed record CleanerInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public CleanerCategory Category { get; init; } = CleanerCategory.System;

    public RiskLevel Risk { get; init; } = RiskLevel.Safe;
}

/// <summary>
/// Cleanup that cannot be expressed as "delete files matching a rule": official Windows cleanup handlers
/// (Windows.old, component store), the Recycle Bin, and so on. Each cleaner estimates and frees space itself.
/// </summary>
public interface ISystemCleaner
{
    CleanerInfo Info { get; }

    /// <summary>Scanning may take minutes (e.g. analysing the component store); run it in the background.</summary>
    bool IsSlow { get; }

    bool RequiresAdministrator { get; }

    Task<SystemScanResult> ScanAsync(CancellationToken cancellationToken);

    Task<SystemCleanResult> CleanAsync(IProgress<SystemCleanProgress>? progress, CancellationToken cancellationToken);
}

public sealed record SystemScanResult
{
    public long Bytes { get; init; }

    /// <summary>False when there is nothing to clean or the cleaner does not apply to this machine.</summary>
    public bool IsApplicable { get; init; }

    /// <summary>Human-readable details (affected folders, item counts).</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static SystemScanResult NotApplicable(params string[] warnings) => new() { IsApplicable = false, Warnings = warnings };
}

public sealed record SystemCleanResult
{
    public long FreedBytes { get; init; }

    public bool Succeeded { get; init; }

    public string? Error { get; init; }

    public static SystemCleanResult Success(long freedBytes) => new() { FreedBytes = freedBytes, Succeeded = true };

    public static SystemCleanResult Failure(string error, long freedBytes = 0) => new() { Error = error, FreedBytes = freedBytes };
}

public readonly record struct SystemCleanProgress(long FreedBytes, long TotalBytes, string? Status);
