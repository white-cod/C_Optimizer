using COptimizer.Core.Environment;
using COptimizer.Windows;

namespace COptimizer.Core.Tests;

/// <summary>Tests against the real Windows APIs. They do not touch existing user data.</summary>
public sealed class WindowsIntegrationTests
{
    private const string ShellTestsVariable = "COPTIMIZER_SHELL_TESTS";

    [Fact]
    public void Profile_provider_returns_current_user_first()
    {
        var profiles = new RegistryProfileProvider().GetProfiles();

        var current = profiles[0];
        Assert.True(current.IsCurrentUser);
        Assert.True(Directory.Exists(current.LocalAppData));
        Assert.Contains(current.PersonalFolders, p => p.EndsWith("Documents", StringComparison.OrdinalIgnoreCase));
        Assert.Single(profiles, p => p.IsCurrentUser);
    }

    [Fact]
    public void System_paths_are_resolved()
    {
        var paths = new WindowsSystemPathsProvider().GetSystemPaths();

        Assert.True(Directory.Exists(paths.WindowsDirectory));
        Assert.True(Directory.Exists(paths.ProfilesDirectory));
        Assert.Equal(Path.GetPathRoot(paths.WindowsDirectory), paths.SystemDrive);
    }

    [Fact]
    public void Real_machine_policy_protects_system_folders()
    {
        var context = CleanupContext.Create(new WindowsSystemPathsProvider(), new RegistryProfileProvider());
        var system = context.System;
        var me = context.Profiles[0];

        Assert.False(context.Policy.CheckTargetRoot(Path.Combine(system.WindowsDirectory, "System32")).IsAllowed);
        Assert.False(context.Policy.CheckTargetRoot(me.ProfilePath).IsAllowed);
        Assert.False(context.Policy.CheckTargetRoot(me.PersonalFolders[0]).IsAllowed);
        Assert.True(context.Policy.CheckTargetRoot(PathVariables.ForUser(system, me).Expand("%TEMP%")).IsAllowed);
    }

    /// <summary>Puts one tiny file into the real Recycle Bin, so it only runs when explicitly enabled.</summary>
    [Fact]
    public void Shell_recycle_bin_moves_file()
    {
        if (System.Environment.GetEnvironmentVariable(ShellTestsVariable) != "1")
            return;

        var path = Path.Combine(Path.GetTempPath(), $"coptimizer-recycle-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "C_Optimizer recycle bin test file - safe to delete.");

        new ShellRecycleBin().MoveToRecycleBin(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Shell_recycle_bin_reports_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coptimizer-missing-{Guid.NewGuid():N}.txt");
        Assert.ThrowsAny<IOException>(() => new ShellRecycleBin().MoveToRecycleBin(path));
    }
}

public sealed class SystemCleanerCatalogTests
{
    [Fact]
    public void Catalog_never_includes_handlers_that_delete_user_data()
    {
        var allowed = COptimizer.Windows.VolumeCaches.VolumeCacheCatalog.AllowedKeyNames.ToArray();

        Assert.DoesNotContain("DownloadsFolder", allowed);
        Assert.DoesNotContain("User file versions", allowed);
        Assert.Empty(allowed.Intersect(COptimizer.Windows.VolumeCaches.VolumeCacheCatalog.Excluded, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void System_cleaners_have_unique_ids_that_do_not_clash_with_rules()
    {
        var cleaners = COptimizer.Windows.WindowsSystemCleaners.Create(@"C:\");
        var ids = cleaners.Select(c => c.Info.Id).ToArray();

        Assert.Contains("windows.recycle-bin", ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Empty(ids.Intersect(COptimizer.Rules.BuiltInRules.All.Select(r => r.Id), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Recycle_bin_scan_is_read_only_and_works_without_elevation()
    {
        var result = await new COptimizer.Windows.RecycleBinCleaner().ScanAsync(CancellationToken.None);
        Assert.True(result.Bytes >= 0);
    }
}
