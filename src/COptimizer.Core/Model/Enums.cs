namespace COptimizer.Core.Model;

/// <summary>How dangerous it is to remove the data covered by a rule.</summary>
public enum RiskLevel
{
    /// <summary>Regenerated automatically, no user-visible loss. Selected by default.</summary>
    Safe,

    /// <summary>Safe for the system, but has a noticeable side effect (e.g. no OS rollback). Not selected by default.</summary>
    Moderate,

    /// <summary>Changes system behaviour or is irreversible. Requires explicit confirmation.</summary>
    Expert,
}

/// <summary>Whether a rule is evaluated once per machine or once per user profile.</summary>
public enum RuleScope
{
    Machine,
    PerUser,
}

/// <summary>What happens to a file when it is cleaned.</summary>
public enum DeleteMode
{
    /// <summary>Removed permanently. Used for caches, temp files, logs and other regenerated data.</summary>
    Permanent,

    /// <summary>Moved to the Recycle Bin so the user can restore it. Used for non-obvious files.</summary>
    RecycleBin,
}

public enum CleanerCategory
{
    System,
    WindowsUpdate,
    Browsers,
    Gpu,
    Games,
    Applications,
    Developer,
    Other,
}
