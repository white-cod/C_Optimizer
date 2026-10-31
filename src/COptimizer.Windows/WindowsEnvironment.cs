using System.Runtime.InteropServices;
using System.Security.Principal;
using COptimizer.Core.Environment;
using Microsoft.Win32;

namespace COptimizer.Windows;

public sealed class WindowsSystemPathsProvider : ISystemPathsProvider
{
    public SystemPaths GetSystemPaths()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return new SystemPaths
        {
            SystemDrive = Path.GetPathRoot(windows) ?? @"C:\",
            WindowsDirectory = windows,
            ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ProgramFiles = programFiles,
            ProgramFilesX86 = string.IsNullOrEmpty(programFilesX86) ? programFiles : programFilesX86,
            ProfilesDirectory = RegistryProfileProvider.ReadProfilesDirectory()
                ?? Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!,
        };
    }
}

/// <summary>
/// Enumerates real user profiles from the registry ProfileList, so per-user rules cover every account on the
/// machine (when running elevated). System/service profiles are excluded.
/// </summary>
public sealed class RegistryProfileProvider : IProfileProvider
{
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    // Local/domain accounts and Microsoft Entra ID (Azure AD) accounts.
    private static readonly string[] UserSidPrefixes = ["S-1-5-21-", "S-1-12-1-"];

    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public IReadOnlyList<UserProfile> GetProfiles()
    {
        var currentSid = WindowsIdentity.GetCurrent().User?.Value;
        var profiles = new List<UserProfile> { CurrentUserProfile() };

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var list = baseKey.OpenSubKey(ProfileListKey);
        if (list is null)
            return profiles;

        foreach (var sid in list.GetSubKeyNames())
        {
            if (!UserSidPrefixes.Any(p => sid.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase))
                continue;

            using var key = list.OpenSubKey(sid);
            if (key?.GetValue("ProfileImagePath") is not string rawPath)
                continue;

            var path = Environment.ExpandEnvironmentVariables(rawPath);
            if (!Directory.Exists(path) || profiles.Any(p => p.ProfilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;

            profiles.Add(UserProfile.FromDefaultLayout(Path.GetFileName(path), path));
        }

        return profiles;
    }

    internal static string? ReadProfilesDirectory()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var list = baseKey.OpenSubKey(ProfileListKey);
        return list?.GetValue("ProfilesDirectory") is string raw ? Environment.ExpandEnvironmentVariables(raw) : null;
    }

    private static UserProfile CurrentUserProfile()
    {
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaults = UserProfile.FromDefaultLayout(Environment.UserName, profilePath, isCurrentUser: true);

        // Personal folders may be redirected (e.g. Documents moved to D:\ or into OneDrive) — protect the real ones too.
        var personal = new List<string>(defaults.PersonalFolders);
        personal.AddRange(new[]
            {
                Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.Favorites,
            }
            .Select(Environment.GetFolderPath));

        if (TryGetKnownFolder(DownloadsFolderId, out var downloads))
            personal.Add(downloads);

        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } oneDrive)
                personal.Add(oneDrive);
        }

        return defaults with
        {
            LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            PersonalFolders = personal.Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    private static bool TryGetKnownFolder(Guid id, out string path)
    {
        path = string.Empty;
        if (NativeMethods.SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var ptr) != 0)
            return false;

        try
        {
            path = Marshal.PtrToStringUni(ptr) ?? string.Empty;
            return path.Length > 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }
}

public static class Elevation
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
