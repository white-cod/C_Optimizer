namespace COptimizer.Core.Environment;

/// <summary>Machine-wide well-known folders. Resolved per machine, never hard-coded.</summary>
public sealed record SystemPaths
{
    /// <summary>System drive root, e.g. "C:\".</summary>
    public required string SystemDrive { get; init; }

    /// <summary>Windows folder, e.g. "C:\Windows".</summary>
    public required string WindowsDirectory { get; init; }

    public required string ProgramData { get; init; }

    public required string ProgramFiles { get; init; }

    /// <summary>Equal to <see cref="ProgramFiles"/> on 32-bit-free systems.</summary>
    public required string ProgramFilesX86 { get; init; }

    /// <summary>Folder that contains user profiles, e.g. "C:\Users".</summary>
    public required string ProfilesDirectory { get; init; }
}

/// <summary>A user profile on the machine. Per-user rules are applied to every profile.</summary>
public sealed record UserProfile
{
    public required string UserName { get; init; }

    /// <summary>Profile root, e.g. "C:\Users\John".</summary>
    public required string ProfilePath { get; init; }

    public required string LocalAppData { get; init; }

    public required string RoamingAppData { get; init; }

    /// <summary>Personal folders (Documents, Desktop, Downloads, ...) that rules must never touch.</summary>
    public IReadOnlyList<string> PersonalFolders { get; init; } = [];

    public bool IsCurrentUser { get; init; }

    /// <summary>Builds a profile using the default folder layout (used for users other than the current one).</summary>
    public static UserProfile FromDefaultLayout(string userName, string profilePath, bool isCurrentUser = false) => new()
    {
        UserName = userName,
        ProfilePath = profilePath,
        LocalAppData = Path.Combine(profilePath, "AppData", "Local"),
        RoamingAppData = Path.Combine(profilePath, "AppData", "Roaming"),
        PersonalFolders = DefaultPersonalFolderNames.Select(n => Path.Combine(profilePath, n)).ToArray(),
        IsCurrentUser = isCurrentUser,
    };

    public static IReadOnlyList<string> DefaultPersonalFolderNames { get; } =
    [
        "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos",
        "OneDrive", "Favorites", "Contacts", "Links", "Saved Games", "Searches",
    ];
}

public interface ISystemPathsProvider
{
    SystemPaths GetSystemPaths();
}

public interface IProfileProvider
{
    IReadOnlyList<UserProfile> GetProfiles();
}
