namespace COptimizer.Core.Model;

/// <summary>
/// Declarative description of a cleanup target: "these folders, these files, under these conditions".
/// Loaded from JSON; see docs/DESIGN.md section 3.2.
/// </summary>
public sealed record RuleDefinition
{
    /// <summary>Stable unique identifier, e.g. "gpu.nvidia.dxcache".</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public CleanerCategory Category { get; init; } = CleanerCategory.Other;

    public RiskLevel Risk { get; init; } = RiskLevel.Safe;

    public RuleScope Scope { get; init; } = RuleScope.Machine;

    public DeleteMode DeleteMode { get; init; } = DeleteMode.Permanent;

    public IReadOnlyList<RuleTarget> Targets { get; init; } = [];

    /// <summary>Only files whose last write time is older than this are removed. 0 = no age limit.</summary>
    public int MinAgeHours { get; init; }

    /// <summary>Process names (without .exe) that lock or rewrite the data; the user is asked to close them.</summary>
    public IReadOnlyList<string> CloseProcesses { get; init; } = [];

    /// <summary>Windows services stopped while the rule is cleaned and started again afterwards (e.g. "wuauserv").</summary>
    public IReadOnlyList<string> StopServices { get; init; } = [];

    /// <summary>Do not touch the data while Windows waits for a restart to finish installing updates.</summary>
    public bool SkipWhenRebootPending { get; init; }
}

/// <summary>One folder (pattern) covered by a rule.</summary>
public sealed record RuleTarget
{
    /// <summary>
    /// Folder path with %VARIABLES%. Path segments may contain * and ? wildcards that match
    /// directory names, e.g. "%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache".
    /// </summary>
    public required string Path { get; init; }

    /// <summary>File name mask, e.g. "*" or "thumbcache_*.db".</summary>
    public string Mask { get; init; } = "*";

    /// <summary>Whether files in subfolders are included.</summary>
    public bool Recursive { get; init; } = true;

    /// <summary>Remove subfolders that become empty after cleaning. The target folder itself is always kept.</summary>
    public bool RemoveEmptyFolders { get; init; } = true;
}
