using COptimizer.Core.Cleaning;
using COptimizer.Core.Environment;
using COptimizer.Core.Model;
using COptimizer.Core.Scanning;

namespace COptimizer.Core.Tests;

public sealed class ServiceRuleTests : IDisposable
{
    private readonly TestMachine _m = new();

    public void Dispose() => _m.Dispose();

    private static readonly RuleDefinition UpdateDownloads = new()
    {
        Id = "windows-update.downloads",
        Name = "Windows Update download cache",
        StopServices = ["wuauserv", "bits"],
        SkipWhenRebootPending = true,
        Targets = [new RuleTarget { Path = @"%WINDIR%\SoftwareDistribution\Download" }],
    };

    [Fact]
    public void Services_are_stopped_during_cleaning_and_restored_after()
    {
        var file = _m.File(@"Windows\SoftwareDistribution\Download\pkg.cab", 1000);
        var services = new FakeServiceManager(() => File.Exists(file));
        var plan = new Scanner(_m.Context).ScanRule(UpdateDownloads);

        var report = new CleanEngine(_m.Context, null, services: services).Clean([plan], new CleanOptions());

        Assert.Equal(1, report.RemovedFiles);
        Assert.Equal(["wuauserv", "bits"], services.StoppedNames);
        Assert.True(services.FileExistedWhenStopped);
        Assert.True(services.Restored);
    }

    [Fact]
    public void Service_problems_are_reported_but_cleaning_continues()
    {
        _m.File(@"Windows\SoftwareDistribution\Download\pkg.cab");
        var services = new FakeServiceManager(() => true) { Problems = ["Service 'bits' could not be stopped."] };
        var plan = new Scanner(_m.Context).ScanRule(UpdateDownloads);

        var report = new CleanEngine(_m.Context, null, services: services).Clean([plan], new CleanOptions());

        Assert.Equal(1, report.RemovedFiles);
        Assert.Contains("bits", Assert.Single(report.Rules[0].Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void Services_are_not_touched_in_dry_run_or_when_nothing_to_clean()
    {
        var services = new FakeServiceManager(() => true);
        var engine = new CleanEngine(_m.Context, null, services: services);

        engine.Clean([new Scanner(_m.Context).ScanRule(UpdateDownloads)], new CleanOptions());
        _m.File(@"Windows\SoftwareDistribution\Download\pkg.cab");
        engine.Clean([new Scanner(_m.Context).ScanRule(UpdateDownloads)], new CleanOptions { DryRun = true });

        Assert.Empty(services.StoppedNames);
    }

    [Fact]
    public void Rule_is_skipped_while_reboot_is_pending()
    {
        _m.File(@"Windows\SoftwareDistribution\Download\pkg.cab");

        var pending = new Scanner(_m.Context, systemState: new FakeSystemState(true)).ScanRule(UpdateDownloads);
        var normal = new Scanner(_m.Context, systemState: new FakeSystemState(false)).ScanRule(UpdateDownloads);

        Assert.Empty(pending.Targets);
        Assert.Contains("restart", Assert.Single(pending.Warnings), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, normal.FileCount);
    }

    private sealed class FakeSystemState(bool rebootPending) : ISystemState
    {
        public bool IsRebootPending() => rebootPending;
    }

    private sealed class FakeServiceManager(Func<bool> probe) : IServiceManager
    {
        public List<string> StoppedNames { get; } = [];

        public bool FileExistedWhenStopped { get; private set; }

        public bool Restored { get; private set; }

        public IReadOnlyList<string> Problems { get; init; } = [];

        public IStoppedServices StopTemporarily(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken)
        {
            StoppedNames.AddRange(serviceNames);
            FileExistedWhenStopped = probe();
            return new Handle(this);
        }

        private sealed class Handle(FakeServiceManager owner) : IStoppedServices
        {
            public IReadOnlyList<string> Problems => owner.Problems;

            public void Dispose() => owner.Restored = true;
        }
    }
}
