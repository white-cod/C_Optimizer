using COptimizer.Core.Environment;

namespace COptimizer.Core.Safety;

/// <summary>
/// Decides which folders the engine is allowed to clean. Every rule target root and every single file is
/// checked against this policy, so a broken or malicious rule cannot reach system or personal data.
/// </summary>
public sealed class SafetyPolicy
{
    /// <summary>Folders inside %WINDIR% that may be cleaned. Anything else under %WINDIR% is off-limits.</summary>
    public static IReadOnlyList<string> WindowsAllowedSubfolders { get; } =
    [
        "Temp",
        "Logs",
        "Minidump",
        "LiveKernelReports",
        "Panther",
        "Debug",
        @"SoftwareDistribution\Download",
        @"SoftwareDistribution\DataStore\Logs",
        @"ServiceProfiles\LocalService\AppData\Local\Temp",
        @"ServiceProfiles\NetworkService\AppData\Local\Temp",
        @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization",
        @"System32\LogFiles\WMI\RtBackup",
    ];

    /// <summary>Folders inside %WINDIR% that are never cleaned, even if an allowed entry would overlap.</summary>
    public static IReadOnlyList<string> WindowsDeniedSubfolders { get; } =
    [
        @"System32\config",
        @"System32\drivers",
        @"System32\DriverStore",
        "SysWOW64",
        "WinSxS",
        "servicing",
        "Boot",
        "Fonts",
        "assembly",
        "Microsoft.NET",
        "SystemApps",
        "SystemResources",
        "Installer",
        "Containers",
    ];

    private readonly string _windowsDirectory;
    private readonly List<string> _protectedExact = [];
    private readonly List<string> _deniedSubtrees = [];
    private readonly List<string> _windowsAllowed;

    public SafetyPolicy(SystemPaths system, IEnumerable<UserProfile> profiles)
    {
        _windowsDirectory = PathUtil.Normalize(system.WindowsDirectory);
        _windowsAllowed = WindowsAllowedSubfolders.Select(s => PathUtil.Normalize(Path.Combine(_windowsDirectory, s))).ToList();

        AddProtected(system.SystemDrive, system.WindowsDirectory, system.ProgramData, system.ProgramFiles,
            system.ProgramFilesX86, system.ProfilesDirectory,
            Path.Combine(system.ProgramData, "Microsoft"),
            Path.Combine(system.ProgramData, "Microsoft", "Windows"));

        foreach (var name in WindowsDeniedSubfolders)
            _deniedSubtrees.Add(PathUtil.Normalize(Path.Combine(_windowsDirectory, name)));

        foreach (var p in profiles)
        {
            AddProtected(p.ProfilePath, Path.Combine(p.ProfilePath, "AppData"), p.LocalAppData, p.RoamingAppData,
                Path.Combine(p.LocalAppData, "Microsoft"), Path.Combine(p.RoamingAppData, "Microsoft"),
                Path.Combine(p.LocalAppData, "Packages"));

            foreach (var personal in p.PersonalFolders)
                _deniedSubtrees.Add(PathUtil.Normalize(personal));
        }
    }

    /// <summary>Checks whether a folder may be used as the root of a cleanup target.</summary>
    public PathCheck CheckTargetRoot(string path)
    {
        if (!PathUtil.IsLocalDrivePath(path))
            return PathCheck.Deny("Only local drive paths are allowed.");

        var normalized = PathUtil.Normalize(path);

        foreach (var p in _protectedExact)
        {
            if (PathUtil.IsSameOrInside(p, normalized))
                return PathCheck.Deny($"Target is or contains protected folder '{p}'.");
        }

        var denied = CheckDeniedAreas(normalized);
        return denied ?? PathCheck.Allow(normalized);
    }

    /// <summary>Checks a single file or subfolder just before it is deleted.</summary>
    public PathCheck CheckItem(string itemPath, string targetRoot)
    {
        if (!PathUtil.IsLocalDrivePath(itemPath))
            return PathCheck.Deny("Only local drive paths are allowed.");

        var normalized = PathUtil.Normalize(itemPath);
        if (!PathUtil.IsStrictlyInside(normalized, PathUtil.Normalize(targetRoot)))
            return PathCheck.Deny("Item is outside of its target folder.");

        return CheckDeniedAreas(normalized) ?? PathCheck.Allow(normalized);
    }

    private PathCheck? CheckDeniedAreas(string normalized)
    {
        foreach (var d in _deniedSubtrees)
        {
            if (PathUtil.IsSameOrInside(normalized, d))
                return PathCheck.Deny($"Path is inside protected area '{d}'.");
        }

        if (PathUtil.IsSameOrInside(normalized, _windowsDirectory) &&
            !_windowsAllowed.Any(a => PathUtil.IsSameOrInside(normalized, a)))
        {
            return PathCheck.Deny("Path is inside the Windows folder but not in an allowed subfolder.");
        }

        return null;
    }

    private void AddProtected(params string[] paths)
    {
        foreach (var path in paths)
            _protectedExact.Add(PathUtil.Normalize(path));
    }
}

public readonly record struct PathCheck(bool IsAllowed, string NormalizedPath, string? Reason)
{
    public static PathCheck Allow(string normalizedPath) => new(true, normalizedPath, null);

    public static PathCheck Deny(string reason) => new(false, string.Empty, reason);
}
