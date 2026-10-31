using System.Diagnostics;
using COptimizer.Core;
using COptimizer.Core.Environment;

namespace COptimizer.Core.Tests;

/// <summary>
/// A fake Windows installation inside a temp folder: system folders and two user profiles.
/// All engine operations in tests run against it, never against the real system.
/// </summary>
public sealed class TestMachine : IDisposable
{
    public TestMachine()
    {
        Root = Path.Combine(Path.GetTempPath(), "coptimizer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        System = new SystemPaths
        {
            SystemDrive = Root,
            WindowsDirectory = Dir("Windows"),
            ProgramData = Dir("ProgramData"),
            ProgramFiles = Dir("Program Files"),
            ProgramFilesX86 = Dir("Program Files (x86)"),
            ProfilesDirectory = Dir("Users"),
        };

        Alice = CreateProfile("alice", isCurrent: true);
        Bob = CreateProfile("bob", isCurrent: false);
        Context = new CleanupContext(System, [Alice, Bob]);
    }

    public string Root { get; }

    public SystemPaths System { get; }

    public UserProfile Alice { get; }

    public UserProfile Bob { get; }

    public CleanupContext Context { get; }

    public string Dir(params string[] parts)
    {
        var path = Path.Combine([Root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    public string File(string relativePath, int size = 100, TimeSpan? age = null)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        global::System.IO.File.WriteAllBytes(path, new byte[size]);
        if (age is { } a)
            global::System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow - a);
        return path;
    }

    /// <summary>Creates a directory junction (works without administrator rights).</summary>
    public static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link))
            throw new InvalidOperationException($"mklink failed: {process.StandardError.ReadToEnd()}");
    }

    public void Dispose()
    {
        try
        {
            // Remove junctions first so deletion never follows them.
            foreach (var dir in Directory.EnumerateDirectories(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
            {
                if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    Directory.Delete(dir);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private UserProfile CreateProfile(string name, bool isCurrent)
    {
        var profile = UserProfile.FromDefaultLayout(name, Dir("Users", name), isCurrent);
        Directory.CreateDirectory(profile.LocalAppData);
        Directory.CreateDirectory(profile.RoamingAppData);
        foreach (var personal in profile.PersonalFolders)
            Directory.CreateDirectory(personal);
        return profile;
    }
}
