using COptimizer.Core.Environment;
using COptimizer.Core.Scanning;

namespace COptimizer.Core.Tests;

public sealed class PathTests : IDisposable
{
    private readonly TestMachine _m = new();

    public void Dispose() => _m.Dispose();

    [Fact]
    public void Expands_user_and_machine_variables()
    {
        var vars = PathVariables.ForUser(_m.System, _m.Bob);

        Assert.Equal(Path.Combine(_m.Bob.LocalAppData, "Temp"), vars.Expand("%TEMP%"));
        Assert.Equal(Path.Combine(_m.Bob.RoamingAppData, "discord"), vars.Expand(@"%APPDATA%\discord"));
        Assert.Equal(Path.Combine(_m.Root, "NVIDIA"), vars.Expand(@"%SYSTEMDRIVE%\NVIDIA"));
        Assert.Equal(Path.Combine(_m.System.ProgramFilesX86, "Steam"), vars.Expand(@"%ProgramFiles(x86)%\Steam"));
    }

    [Fact]
    public void Machine_context_rejects_user_variables()
    {
        var vars = PathVariables.ForMachine(_m.System);
        Assert.Throws<InvalidOperationException>(() => vars.Expand(@"%LOCALAPPDATA%\x"));
    }

    [Fact]
    public void Reports_unknown_variables()
    {
        Assert.Equal(["HOMEPATH"], PathVariables.FindUnknownVariables(@"%HOMEPATH%\x"));
        Assert.Empty(PathVariables.FindUnknownVariables(@"%LOCALAPPDATA%\%USERNAME%"));
        Assert.True(PathVariables.UsesUserVariables(@"%LOCALAPPDATA%\x"));
        Assert.False(PathVariables.UsesUserVariables(@"%WINDIR%\Temp"));
    }

    [Fact]
    public void Wildcard_segments_match_existing_directories()
    {
        var userData = _m.Dir("Users", "alice", "AppData", "Local", "Google", "Chrome", "User Data");
        _m.Dir("Users", "alice", "AppData", "Local", "Google", "Chrome", "User Data", "Default", "Cache");
        _m.Dir("Users", "alice", "AppData", "Local", "Google", "Chrome", "User Data", "Profile 1", "Cache");
        _m.Dir("Users", "alice", "AppData", "Local", "Google", "Chrome", "User Data", "Crashpad");

        var result = PathPatternExpander.Expand(Path.Combine(userData, "*", "Cache"));

        Assert.Equal(2, result.Count);
        Assert.Contains(Path.Combine(userData, "Default", "Cache"), result);
        Assert.Contains(Path.Combine(userData, "Profile 1", "Cache"), result);
    }

    [Fact]
    public void Pattern_never_resolves_through_a_junction()
    {
        var documents = Path.Combine(_m.Alice.ProfilePath, "Documents");
        _m.Dir("Users", "alice", "Documents", "Cache");
        var vendor = _m.Dir("Users", "alice", "AppData", "Local", "Vendor");
        TestMachine.CreateJunction(Path.Combine(vendor, "Linked"), documents);

        Assert.Empty(PathPatternExpander.Expand(Path.Combine(vendor, "Linked", "Cache")));
        Assert.Empty(PathPatternExpander.Expand(Path.Combine(vendor, "*", "Cache")));
    }

    [Fact]
    public void Missing_paths_resolve_to_nothing() =>
        Assert.Empty(PathPatternExpander.Expand(Path.Combine(_m.Root, "does", "not", "exist")));
}
