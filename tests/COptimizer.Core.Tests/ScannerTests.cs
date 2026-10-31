using COptimizer.Core.Model;
using COptimizer.Core.Processes;
using COptimizer.Core.Scanning;

namespace COptimizer.Core.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly TestMachine _m = new();

    public void Dispose() => _m.Dispose();

    private static RuleDefinition Rule(string path, RuleScope scope = RuleScope.PerUser, string mask = "*",
        bool recursive = true, int minAgeHours = 0) => new()
    {
        Id = "test.rule",
        Name = "Test rule",
        Scope = scope,
        MinAgeHours = minAgeHours,
        Targets = [new RuleTarget { Path = path, Mask = mask, Recursive = recursive }],
    };

    [Fact]
    public void Per_user_rule_covers_every_profile()
    {
        _m.File(@"Users\alice\AppData\Local\NVIDIA\DXCache\a.bin", 1000);
        _m.File(@"Users\bob\AppData\Local\NVIDIA\DXCache\sub\b.bin", 500);

        var result = new Scanner(_m.Context).ScanRule(Rule(@"%LOCALAPPDATA%\NVIDIA\DXCache"));

        Assert.Equal(2, result.Targets.Count);
        Assert.Equal(1500, result.TotalBytes);
        Assert.Equal(["alice", "bob"], result.Targets.Select(t => t.UserName).Order());
    }

    [Fact]
    public void Applies_mask_and_recursion_settings()
    {
        _m.File(@"Users\alice\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db");
        _m.File(@"Users\alice\AppData\Local\Microsoft\Windows\Explorer\other.db");
        _m.File(@"Users\alice\AppData\Local\Microsoft\Windows\Explorer\sub\thumbcache_32.db");

        var result = new Scanner(_m.Context).ScanRule(
            Rule(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", mask: "thumbcache_*.db", recursive: false));

        var file = Assert.Single(result.Targets.SelectMany(t => t.Files));
        Assert.EndsWith("thumbcache_256.db", file.Path);
    }

    [Fact]
    public void Respects_minimum_age()
    {
        _m.File(@"Windows\Temp\old.tmp", age: TimeSpan.FromDays(3));
        _m.File(@"Windows\Temp\fresh.tmp", age: TimeSpan.FromMinutes(5));

        var result = new Scanner(_m.Context).ScanRule(Rule(@"%WINDIR%\Temp", RuleScope.Machine, minAgeHours: 24));

        var file = Assert.Single(result.Targets.SelectMany(t => t.Files));
        Assert.EndsWith("old.tmp", file.Path);
    }

    [Fact]
    public void Does_not_descend_into_junctions()
    {
        var secret = _m.File(@"Users\alice\Documents\important.docx", 5000);
        _m.File(@"Users\alice\AppData\Local\App\Cache\data.bin", 10);
        TestMachine.CreateJunction(
            Path.Combine(_m.Alice.LocalAppData, "App", "Cache", "link"),
            Path.GetDirectoryName(secret)!);

        var result = new Scanner(_m.Context).ScanRule(Rule(@"%LOCALAPPDATA%\App\Cache"));

        var file = Assert.Single(result.Targets.SelectMany(t => t.Files));
        Assert.EndsWith("data.bin", file.Path);
    }

    [Fact]
    public void Protected_target_is_reported_and_skipped()
    {
        _m.File(@"Users\alice\Documents\cv.docx");

        var result = new Scanner(_m.Context).ScanRule(Rule(@"%USERPROFILE%\Documents"));

        Assert.Empty(result.Targets);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Same_folder_from_two_targets_is_scanned_once()
    {
        _m.File(@"Users\alice\AppData\Local\Temp\a.tmp");
        var rule = Rule(@"%TEMP%") with
        {
            Targets = [new RuleTarget { Path = "%TEMP%" }, new RuleTarget { Path = @"%LOCALAPPDATA%\Temp" }],
        };

        var result = new Scanner(_m.Context).ScanRule(rule);

        Assert.Single(result.Targets, t => t.UserName == "alice");
        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public void Reports_running_processes_only_when_rule_applies()
    {
        var inspector = new FakeProcessInspector("chrome");
        var rule = Rule(@"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache") with { CloseProcesses = ["chrome"] };
        var scanner = new Scanner(_m.Context, inspector);

        Assert.Empty(scanner.ScanRule(rule).RunningProcesses);

        _m.File(@"Users\alice\AppData\Local\Google\Chrome\User Data\Default\Cache\f_0001");
        Assert.Equal(["chrome"], scanner.ScanRule(rule).RunningProcesses);
    }

    private sealed class FakeProcessInspector(params string[] running) : IProcessInspector
    {
        public IReadOnlyList<string> GetRunning(IEnumerable<string> processNames) =>
            processNames.Where(n => running.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();
    }
}
