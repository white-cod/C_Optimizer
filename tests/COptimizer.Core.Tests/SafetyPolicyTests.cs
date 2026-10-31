namespace COptimizer.Core.Tests;

public sealed class SafetyPolicyTests : IDisposable
{
    private readonly TestMachine _m = new();

    public void Dispose() => _m.Dispose();

    [Theory]
    [InlineData("")]                                   // system drive root
    [InlineData("Windows")]
    [InlineData("Windows\\System32")]
    [InlineData("Windows\\System32\\drivers")]
    [InlineData("Windows\\WinSxS")]
    [InlineData("Windows\\WinSxS\\Temp")]
    [InlineData("Windows\\Prefetch")]                  // inside Windows but not allow-listed
    [InlineData("Windows\\Installer")]
    [InlineData("Program Files")]
    [InlineData("ProgramData")]
    [InlineData("ProgramData\\Microsoft")]
    [InlineData("Users")]
    [InlineData("Users\\alice")]
    [InlineData("Users\\alice\\AppData")]
    [InlineData("Users\\alice\\AppData\\Local")]
    [InlineData("Users\\alice\\AppData\\Roaming")]
    [InlineData("Users\\alice\\AppData\\Local\\Packages")]
    [InlineData("Users\\alice\\Documents")]
    [InlineData("Users\\alice\\Documents\\Cache")]
    [InlineData("Users\\bob\\Desktop\\temp")]
    [InlineData("Users\\bob\\Saved Games\\Game\\Cache")]
    [InlineData("Users\\alice\\AppData\\Local\\Temp\\..\\..")]   // traversal normalizes to a protected folder
    public void Denies_protected_targets(string relative)
    {
        var check = _m.Context.Policy.CheckTargetRoot(Path.Combine(_m.Root, relative));
        Assert.False(check.IsAllowed, $"'{relative}' must be denied");
    }

    [Theory]
    [InlineData("Windows\\Temp")]
    [InlineData("Windows\\Logs\\CBS")]
    [InlineData("Windows\\SoftwareDistribution\\Download")]
    [InlineData("Users\\alice\\AppData\\Local\\Temp")]
    [InlineData("Users\\bob\\AppData\\Local\\NVIDIA\\DXCache")]
    [InlineData("Users\\alice\\AppData\\Local\\Packages\\App_123\\AC\\INetCache")]
    [InlineData("ProgramData\\NVIDIA Corporation\\NV_Cache")]
    [InlineData("Program Files (x86)\\Steam\\logs")]
    [InlineData("NVIDIA")]
    public void Allows_cleanup_targets(string relative)
    {
        var check = _m.Context.Policy.CheckTargetRoot(Path.Combine(_m.Root, relative));
        Assert.True(check.IsAllowed, check.Reason);
    }

    [Theory]
    [InlineData(@"\\server\share\cache")]
    [InlineData(@"relative\path")]
    [InlineData(@"\\?\C:\Temp")]
    public void Denies_non_local_paths(string path) =>
        Assert.False(_m.Context.Policy.CheckTargetRoot(path).IsAllowed);

    [Fact]
    public void Item_must_be_strictly_inside_its_target()
    {
        var root = Path.Combine(_m.Root, "Windows", "Temp");
        var policy = _m.Context.Policy;

        Assert.True(policy.CheckItem(Path.Combine(root, "a", "b.tmp"), root).IsAllowed);
        Assert.False(policy.CheckItem(root, root).IsAllowed);
        Assert.False(policy.CheckItem(Path.Combine(root, "..", "System32", "kernel32.dll"), root).IsAllowed);
        Assert.False(policy.CheckItem(Path.Combine(_m.Root, "Windows", "TempOther", "x.tmp"), root).IsAllowed);
    }
}
