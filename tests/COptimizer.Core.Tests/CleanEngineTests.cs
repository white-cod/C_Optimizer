using COptimizer.Core.Cleaning;
using COptimizer.Core.Model;
using COptimizer.Core.Scanning;

namespace COptimizer.Core.Tests;

public sealed class CleanEngineTests : IDisposable
{
    private readonly TestMachine _m = new();

    public void Dispose() => _m.Dispose();

    private static RuleDefinition CacheRule(DeleteMode mode = DeleteMode.Permanent) => new()
    {
        Id = "test.cache",
        Name = "Test cache",
        Scope = RuleScope.PerUser,
        DeleteMode = mode,
        Targets = [new RuleTarget { Path = @"%LOCALAPPDATA%\App\Cache" }],
    };

    private RuleScanResult Scan(RuleDefinition rule) => new Scanner(_m.Context).ScanRule(rule);

    [Fact]
    public void Removes_files_and_empty_subfolders_but_keeps_target_folder()
    {
        _m.File(@"Users\alice\AppData\Local\App\Cache\a.bin", 300);
        _m.File(@"Users\alice\AppData\Local\App\Cache\deep\er\b.bin", 200);
        var cache = Path.Combine(_m.Alice.LocalAppData, "App", "Cache");

        var report = new CleanEngine(_m.Context, null).Clean([Scan(CacheRule())], new CleanOptions());

        Assert.Equal(2, report.RemovedFiles);
        Assert.Equal(500, report.RemovedBytes);
        Assert.True(Directory.Exists(cache));
        Assert.Empty(Directory.EnumerateFileSystemEntries(cache));
    }

    [Fact]
    public void Dry_run_changes_nothing()
    {
        var file = _m.File(@"Users\alice\AppData\Local\App\Cache\a.bin", 300);

        var report = new CleanEngine(_m.Context, null).Clean([Scan(CacheRule())], new CleanOptions { DryRun = true });

        Assert.True(report.DryRun);
        Assert.Equal(300, report.RemovedBytes);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Skips_files_changed_after_scan()
    {
        var file = _m.File(@"Users\alice\AppData\Local\App\Cache\a.bin", 300);
        var plan = Scan(CacheRule());
        File.WriteAllBytes(file, new byte[999]);

        var report = new CleanEngine(_m.Context, null).Clean([plan], new CleanOptions());

        Assert.True(File.Exists(file));
        Assert.Equal(SkipReason.ChangedSinceScan, Assert.Single(report.Rules[0].Skipped).Reason);
    }

    [Fact]
    public void Skips_locked_files_and_continues()
    {
        var locked = _m.File(@"Users\alice\AppData\Local\App\Cache\locked.bin");
        var free = _m.File(@"Users\alice\AppData\Local\App\Cache\free.bin");
        var plan = Scan(CacheRule());

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = new CleanEngine(_m.Context, null).Clean([plan], new CleanOptions());

            Assert.Equal(1, report.RemovedFiles);
            Assert.Equal(SkipReason.InUseOrAccessDenied, Assert.Single(report.Rules[0].Skipped).Reason);
        }

        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free));
    }

    [Fact]
    public void Removes_read_only_files()
    {
        var file = _m.File(@"Users\alice\AppData\Local\App\Cache\ro.bin");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var report = new CleanEngine(_m.Context, null).Clean([Scan(CacheRule())], new CleanOptions());

        Assert.Equal(1, report.RemovedFiles);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Folder_replaced_by_junction_after_scan_is_not_followed()
    {
        _m.File(@"Users\alice\AppData\Local\App\Cache\sub\victim.docx", 400);
        var plan = Scan(CacheRule());

        // Attacker/app swaps the scanned folder for a junction into Documents that holds a same-named file.
        var sub = Path.Combine(_m.Alice.LocalAppData, "App", "Cache", "sub");
        var documents = Path.Combine(_m.Alice.ProfilePath, "Documents");
        File.Move(Path.Combine(sub, "victim.docx"), Path.Combine(documents, "victim.docx"));
        File.SetLastWriteTimeUtc(Path.Combine(documents, "victim.docx"), plan.Targets[0].Files[0].LastWriteTimeUtc);
        Directory.Delete(sub);
        TestMachine.CreateJunction(sub, documents);

        var report = new CleanEngine(_m.Context, null).Clean([plan], new CleanOptions());

        Assert.Equal(0, report.RemovedFiles);
        Assert.Equal(SkipReason.SafetyCheckFailed, Assert.Single(report.Rules[0].Skipped).Reason);
        Assert.True(File.Exists(Path.Combine(documents, "victim.docx")));
    }

    [Fact]
    public void Recycle_bin_mode_uses_recycle_bin()
    {
        var file = _m.File(@"Users\alice\AppData\Local\App\Cache\a.bin");
        var bin = new FakeRecycleBin();

        var report = new CleanEngine(_m.Context, bin).Clean([Scan(CacheRule(DeleteMode.RecycleBin))], new CleanOptions());

        Assert.Equal(1, report.RemovedFiles);
        Assert.Equal([file], bin.Recycled);
    }

    [Fact]
    public void Recycle_bin_failure_keeps_file()
    {
        var file = _m.File(@"Users\alice\AppData\Local\App\Cache\big.bin");
        var bin = new FakeRecycleBin { Fail = true };

        var report = new CleanEngine(_m.Context, bin).Clean([Scan(CacheRule(DeleteMode.RecycleBin))], new CleanOptions());

        Assert.Equal(0, report.RemovedFiles);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Already_removed_files_are_not_counted_as_skipped()
    {
        var file = _m.File(@"Users\alice\AppData\Local\App\Cache\a.bin");
        var plan = Scan(CacheRule());
        File.Delete(file);

        var report = new CleanEngine(_m.Context, null).Clean([plan], new CleanOptions());

        Assert.Equal(0, report.SkippedFiles);
        Assert.Equal(SkipReason.Missing, Assert.Single(report.Rules[0].Skipped).Reason);
    }

    private sealed class FakeRecycleBin : IRecycleBin
    {
        public bool Fail { get; init; }

        public List<string> Recycled { get; } = [];

        public void MoveToRecycleBin(string path)
        {
            if (Fail)
                throw new IOException("Too large for the Recycle Bin.");
            Recycled.Add(path);
            File.Delete(path);
        }
    }
}
