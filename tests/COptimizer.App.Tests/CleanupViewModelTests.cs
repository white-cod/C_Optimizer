using COptimizer.App.Services;
using COptimizer.App.ViewModels;
using COptimizer.Core.Cleaning;
using COptimizer.Core.Model;
using COptimizer.Core.Scanning;
using COptimizer.Core.SystemCleaners;

namespace COptimizer.App.Tests;

public sealed class CleanupViewModelTests
{
    private static RuleDefinition Rule(string id, CleanerCategory category, RiskLevel risk = RiskLevel.Safe,
        params string[] closeProcesses) => new()
    {
        Id = id,
        Name = id,
        Description = $"{id} description",
        Category = category,
        Risk = risk,
        CloseProcesses = closeProcesses,
        Targets = [new RuleTarget { Path = @"%WINDIR%\Temp" }],
    };

    private static readonly RuleDefinition NvidiaCache = Rule("gpu.nvidia", CleanerCategory.Gpu);
    private static readonly RuleDefinition AmdCache = Rule("gpu.amd", CleanerCategory.Gpu);
    private static readonly RuleDefinition ChromeCache = Rule("browser.chrome", CleanerCategory.Browsers, RiskLevel.Safe, "chrome");
    private static readonly RuleDefinition NpmCache = Rule("dev.npm", CleanerCategory.Developer, RiskLevel.Moderate);
    private static readonly RuleDefinition EmptyRule = Rule("app.empty", CleanerCategory.Applications);

    private static FakeCleanupService CreateService(params FakeSystemCleaner[] systemCleaners) => new()
    {
        Rules = [NvidiaCache, AmdCache, ChromeCache, NpmCache, EmptyRule],
        SystemCleaners = systemCleaners,
        Sizes =
        {
            [NvidiaCache.Id] = 11_000,
            [AmdCache.Id] = 500,
            [ChromeCache.Id] = 700,
            [NpmCache.Id] = 4_000,
            [EmptyRule.Id] = 0,
        },
        Running = { [ChromeCache.Id] = ["chrome"] },
    };

    [Fact]
    public async Task Scan_groups_results_and_preselects_only_safe_items()
    {
        var vm = new CleanupViewModel(CreateService(), new FakeShell());

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.HasScanned);
        Assert.Equal([CleanerCategory.Browsers, CleanerCategory.Gpu, CleanerCategory.Developer], vm.Groups.Select(g => g.Category));
        Assert.DoesNotContain(vm.AllItems, r => r.Id == EmptyRule.Id);
        Assert.Equal(["gpu.nvidia", "gpu.amd"], vm.Groups.Single(g => g.Category == CleanerCategory.Gpu).Items.Select(r => r.Id));

        Assert.False(vm.AllItems.Single(r => r.Id == NpmCache.Id).IsSelected);
        Assert.Equal(16_200, vm.TotalBytes);
        Assert.Equal(12_200, vm.RecommendedBytes);
        Assert.Equal(12_200, vm.SelectedBytes);
        Assert.Equal(3, vm.SelectedCount);
    }

    [Fact]
    public async Task Group_checkbox_is_tri_state_and_selects_children()
    {
        var vm = new CleanupViewModel(CreateService(), new FakeShell());
        await vm.ScanCommand.ExecuteAsync(null);
        var gpu = vm.Groups.Single(g => g.Category == CleanerCategory.Gpu);

        Assert.True(gpu.IsSelected);
        gpu.Items[1].IsSelected = false;
        Assert.Null(gpu.IsSelected);
        Assert.Equal(11_700, vm.SelectedBytes);

        gpu.IsSelected = false;
        Assert.False(gpu.IsSelected);
        Assert.Equal(700, vm.SelectedBytes);

        vm.SelectAllCommand.Execute(null);
        Assert.Equal(16_200, vm.SelectedBytes);
        vm.SelectNoneCommand.Execute(null);
        Assert.False(vm.CleanCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selection_survives_rescan()
    {
        var vm = new CleanupViewModel(CreateService(), new FakeShell());
        await vm.ScanCommand.ExecuteAsync(null);
        vm.AllItems.Single(r => r.Id == NpmCache.Id).IsSelected = true;
        vm.AllItems.Single(r => r.Id == NvidiaCache.Id).IsSelected = false;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.AllItems.Single(r => r.Id == NpmCache.Id).IsSelected);
        Assert.False(vm.AllItems.Single(r => r.Id == NvidiaCache.Id).IsSelected);
    }

    [Fact]
    public async Task Declined_confirmation_removes_nothing()
    {
        var service = CreateService();
        var vm = new CleanupViewModel(service, new FakeShell { Confirm = false });
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Null(service.CleanedPlan);
        Assert.Null(vm.LastCleanSummary);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Clean_uses_a_fresh_plan_of_selected_items_then_rescans()
    {
        var service = CreateService();
        var shell = new FakeShell();
        var vm = new CleanupViewModel(service, shell);
        var finished = 0;
        vm.CleanFinished += (_, _) => finished++;
        await vm.ScanCommand.ExecuteAsync(null);
        vm.AllItems.Single(r => r.Id == AmdCache.Id).IsSelected = false;
        var scansBefore = service.ScanCount;

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal([ChromeCache.Id, NvidiaCache.Id], service.CleanedPlan!.Rules.Select(r => r.Rule.Id).Order());
        Assert.Equal(scansBefore + 2, service.ScanCount); // fresh plan + refresh afterwards
        Assert.Equal(1, finished);
        Assert.Equal("Freed 11.4 KB", vm.LastCleanSummary!.Title);
        Assert.Contains("chrome", shell.LastMessage, StringComparison.Ordinal);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Clean_recommended_resets_selection_to_safe_items()
    {
        var service = CreateService();
        var vm = new CleanupViewModel(service, new FakeShell());
        await vm.ScanCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);

        await vm.CleanRecommendedCommand.ExecuteAsync(null);

        Assert.DoesNotContain(service.CleanedPlan!.Rules, r => r.Rule.Id == NpmCache.Id);
        Assert.Equal(3, service.CleanedPlan!.Rules.Count);
    }

    [Fact]
    public async Task System_cleaners_appear_as_items_with_risk_based_selection()
    {
        var updates = new FakeSystemCleaner("windows.delivery-optimization", CleanerCategory.WindowsUpdate, RiskLevel.Safe, 2_000);
        var windowsOld = new FakeSystemCleaner("windows.previous-installations", CleanerCategory.System, RiskLevel.Moderate, 20_000);
        var nothing = new FakeSystemCleaner("windows.retail-demo", CleanerCategory.System, RiskLevel.Safe, 0);
        var vm = new CleanupViewModel(CreateService(updates, windowsOld, nothing), new FakeShell());

        await vm.ScanCommand.ExecuteAsync(null);

        var updateItem = Assert.IsType<SystemItemViewModel>(vm.AllItems.Single(i => i.Id == updates.Info.Id));
        Assert.True(updateItem.IsSelected);
        Assert.False(vm.AllItems.Single(i => i.Id == windowsOld.Info.Id).IsSelected);
        Assert.DoesNotContain(vm.AllItems, i => i.Id == nothing.Info.Id);
        Assert.Equal(CleanerCategory.System, vm.Groups[0].Category);
        Assert.Equal(38_200, vm.TotalBytes);
    }

    [Fact]
    public async Task Slow_cleaners_are_analyzed_in_background_and_added_later()
    {
        var slow = new FakeSystemCleaner("windows.update-cleanup", CleanerCategory.WindowsUpdate, RiskLevel.Safe, 5_000) { Gate = new() };
        var vm = new CleanupViewModel(CreateService(slow), new FakeShell());

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.IsAnalyzingSystem);
        Assert.False(vm.IsBusy);
        Assert.DoesNotContain(vm.AllItems, i => i.Id == slow.Info.Id);

        slow.Gate.SetResult();
        await vm.AnalysisTask;

        Assert.False(vm.IsAnalyzingSystem);
        var item = vm.AllItems.Single(i => i.Id == slow.Info.Id);
        Assert.True(item.IsSelected);
        Assert.Equal(17_200, vm.SelectedBytes);
        Assert.Contains(vm.Groups, g => g.Category == CleanerCategory.WindowsUpdate);
    }

    [Fact]
    public async Task Clean_includes_selected_system_cleaners_with_estimates()
    {
        var windowsOld = new FakeSystemCleaner("windows.previous-installations", CleanerCategory.System, RiskLevel.Moderate, 20_000);
        var service = CreateService(windowsOld);
        var shell = new FakeShell();
        var vm = new CleanupViewModel(service, shell);
        await vm.ScanCommand.ExecuteAsync(null);
        vm.SelectNoneCommand.Execute(null);
        vm.AllItems.Single(i => i.Id == windowsOld.Info.Id).IsSelected = true;

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Empty(service.CleanedPlan!.Rules);
        var selection = Assert.Single(service.CleanedPlan.SystemCleaners);
        Assert.Same(windowsOld, selection.Cleaner);
        Assert.Equal(20_000, selection.EstimatedBytes);
        Assert.Contains("Windows cleanup tools", shell.LastMessage, StringComparison.Ordinal);
        Assert.Contains("windows.previous-installations description", shell.LastMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleaning_cancels_background_analysis()
    {
        var slow = new FakeSystemCleaner("windows.update-cleanup", CleanerCategory.WindowsUpdate, RiskLevel.Safe, 5_000) { Gate = new() };
        var vm = new CleanupViewModel(CreateService(slow), new FakeShell { Confirm = false });
        await vm.ScanCommand.ExecuteAsync(null);
        var firstAnalysis = vm.AnalysisTask;

        await vm.CleanCommand.ExecuteAsync(null);

        await firstAnalysis; // completes because it was cancelled, not because the gate opened
        Assert.True(slow.WasCancelled);
    }

    [Fact]
    public void Confirmation_lists_side_effects_and_running_apps()
    {
        ConfirmationEntry[] entries =
        [
            new("npm cache", "dev.npm description", RiskLevel.Moderate, 4_000, false, false, []),
            new("Chrome cache", "chrome description", RiskLevel.Safe, 700, false, false, ["chrome"]),
        ];

        var text = CleanupViewModel.BuildConfirmation(entries);

        Assert.Contains("About 4.6 KB will be freed by cleaning 2 items", text, StringComparison.Ordinal);
        Assert.Contains("Items with side effects", text, StringComparison.Ordinal);
        Assert.Contains("dev.npm description", text, StringComparison.Ordinal);
        Assert.Contains("Running apps: chrome", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows cleanup tools", text, StringComparison.Ordinal);
    }

    private sealed class FakeSystemCleaner(string id, CleanerCategory category, RiskLevel risk, long bytes) : ISystemCleaner
    {
        public CleanerInfo Info { get; } = new() { Id = id, Name = id, Description = $"{id} description", Category = category, Risk = risk };

        public bool IsSlow => Gate is not null;

        public bool RequiresAdministrator => true;

        /// <summary>When set, the scan waits for it (simulates a slow component store analysis).</summary>
        public TaskCompletionSource? Gate { get; init; }

        public bool WasCancelled { get; private set; }

        public async Task<SystemScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            if (Gate is not null)
            {
                try
                {
                    await Gate.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    throw;
                }
            }

            return new SystemScanResult { Bytes = bytes, IsApplicable = bytes > 0 };
        }

        public Task<SystemCleanResult> CleanAsync(IProgress<SystemCleanProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(SystemCleanResult.Success(bytes));
    }

    private sealed class FakeCleanupService : ICleanupService
    {
        public bool IsAdministrator => true;

        public required IReadOnlyList<RuleDefinition> Rules { get; init; }

        public IReadOnlyList<ISystemCleaner> SystemCleaners { get; init; } = [];

        public Dictionary<string, long> Sizes { get; } = [];

        public Dictionary<string, string[]> Running { get; } = [];

        public int ScanCount { get; private set; }

        public CleanPlan? CleanedPlan { get; private set; }

        private static RuleScanResult Result(RuleDefinition rule, long size, string[] running) => new()
        {
            Rule = rule,
            Targets = size == 0
                ? []
                : [new TargetScanResult
                {
                    Root = $@"C:\fake\{rule.Id}",
                    Target = rule.Targets[0],
                    Files = [new FoundFile($@"C:\fake\{rule.Id}\file.bin", size, DateTime.UtcNow)],
                }],
            RunningProcesses = running,
        };

        public DiskUsage GetSystemDriveUsage() => new("C:", 100_000, 40_000);

        public Task<IReadOnlyList<RuleScanResult>> ScanAsync(
            IReadOnlyList<RuleDefinition> rules, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        {
            ScanCount++;
            IReadOnlyList<RuleScanResult> results = rules
                .Select(r => Result(r, Sizes.GetValueOrDefault(r.Id), Running.GetValueOrDefault(r.Id) ?? []))
                .ToArray();
            return Task.FromResult(results);
        }

        public Task<CleanOutcome> CleanAsync(CleanPlan plan, IProgress<CleanStatus>? progress, CancellationToken cancellationToken)
        {
            CleanedPlan = plan;
            var report = new CleanReport { DryRun = false, StartedAt = DateTimeOffset.UtcNow };
            foreach (var r in plan.Rules)
                report.Rules.Add(new RuleCleanReport { RuleId = r.Rule.Id, RuleName = r.Rule.Name, RemovedBytes = r.TotalBytes });

            var system = plan.SystemCleaners
                .Select(s => new SystemCleanOutcome(s.Cleaner.Info, SystemCleanResult.Success(s.EstimatedBytes)))
                .ToArray();
            return Task.FromResult(new CleanOutcome(report, system, null));
        }
    }

    private sealed class FakeShell : IShellService
    {
        public bool Confirm { get; init; } = true;

        public string LastMessage { get; private set; } = string.Empty;

        public Task<bool> ConfirmAsync(string title, string message, string confirmText)
        {
            LastMessage = message;
            return Task.FromResult(Confirm);
        }

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;

        public void OpenInExplorer(string path)
        {
        }

        public bool RestartAsAdministrator() => false;
    }
}
