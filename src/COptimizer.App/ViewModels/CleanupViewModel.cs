using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using COptimizer.App.Services;
using COptimizer.Core;
using COptimizer.Core.Model;
using COptimizer.Core.Scanning;
using COptimizer.Core.SystemCleaners;

namespace COptimizer.App.ViewModels;

/// <summary>Information shown in the confirmation dialog for one item of a clean plan.</summary>
internal sealed record ConfirmationEntry(
    string Name, string Description, RiskLevel Risk, long Bytes, bool ToRecycleBin, bool IsSystemTool, IReadOnlyList<string> RunningProcesses);

/// <summary>Scan results, selection and the clean workflow. Shared by the Home and Cleanup pages.</summary>
[SuppressMessage("Design", "CA1001", Justification = "CancellationTokenSources are owned and disposed by the operations that create them.")]
public sealed partial class CleanupViewModel : ObservableObject
{
    private readonly ICleanupService _service;
    private readonly IShellService _shell;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _analysisCts;

    public CleanupViewModel(ICleanupService service, IShellService shell)
    {
        _service = service;
        _shell = shell;
    }

    /// <summary>Raised after something was cleaned (the disk usage should be refreshed).</summary>
    public event EventHandler? CleanFinished;

    public ObservableCollection<CategoryGroupViewModel> Groups { get; } = [];

    public IEnumerable<CleanupItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(CleanRecommendedCommand))]
    public partial bool IsScanning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(CleanRecommendedCommand))]
    public partial bool IsCleaning { get; private set; }

    public bool IsBusy => IsScanning || IsCleaning;

    /// <summary>Slow system checks (e.g. the component store) are still running in the background.</summary>
    [ObservableProperty]
    public partial bool IsAnalyzingSystem { get; private set; }

    [ObservableProperty]
    public partial string AnalysisText { get; private set; } = string.Empty;

    /// <summary>True after the first scan finished, even if nothing was found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNothingToClean))]
    public partial bool HasScanned { get; private set; }

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    [ObservableProperty]
    public partial CleanSummaryViewModel? LastCleanSummary { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalText), nameof(HasNothingToClean))]
    public partial long TotalBytes { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecommendedText))]
    [NotifyCanExecuteChangedFor(nameof(CleanRecommendedCommand))]
    public partial long RecommendedBytes { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    public partial long SelectedBytes { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    public partial int SelectedCount { get; private set; }

    public string TotalText => ByteSize.Format(TotalBytes);

    public string RecommendedText => ByteSize.Format(RecommendedBytes);

    public string SelectedText => SelectedCount == 0
        ? "Nothing selected"
        : $"{ByteSize.Format(SelectedBytes)} selected in {SelectedCount} item{(SelectedCount == 1 ? "" : "s")}";

    public bool HasNothingToClean => HasScanned && TotalBytes == 0 && !IsAnalyzingSystem;

    private bool CanStart => !IsBusy;

    private bool CanClean => !IsBusy && SelectedCount > 0;

    private bool CanCleanRecommended => !IsBusy && RecommendedBytes > 0;

    /// <summary>Completes when the background analysis started by the last scan has finished (used by tests).</summary>
    internal Task AnalysisTask { get; private set; } = Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task ScanAsync()
    {
        CancelAnalysis();
        using var cts = BeginOperation();
        IsScanning = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        ProgressText = "Scanning…";

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressValue = p.TotalRules == 0 ? 100 : p.CompletedRules * 100.0 / p.TotalRules;
            ProgressText = $"Scanning: {p.CurrentRuleName}";
        });

        var slowCleaners = _service.SystemCleaners.Where(c => c.IsSlow).ToArray();
        try
        {
            var rulesTask = _service.ScanAsync(_service.Rules, progress, cts.Token);
            var systemTask = ScanSystemCleanersAsync(_service.SystemCleaners.Where(c => !c.IsSlow), cts.Token);
            await Task.WhenAll(rulesTask, systemTask);

            ApplyResults(rulesTask.Result, systemTask.Result);
            HasScanned = true;
        }
        catch (OperationCanceledException)
        {
            slowCleaners = []; // Keep the previous results; no background analysis.
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            slowCleaners = [];
            await _shell.ShowErrorAsync("Scan failed", e.Message);
        }
        finally
        {
            IsScanning = false;
            EndOperation();
        }

        if (slowCleaners.Length > 0)
            AnalysisTask = AnalyzeInBackgroundAsync(slowCleaners);
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selected = AllItems.Where(i => i.IsSelected).ToArray();
        if (selected.Length == 0)
            return;

        // Background analysis must not run while Windows cleanup handlers purge; the refresh afterwards restarts it.
        CancelAnalysis();

        var cleaned = false;
        using (var cts = BeginOperation())
        {
            IsCleaning = true;
            IsProgressIndeterminate = true;
            ProgressText = "Preparing…";

            try
            {
                var plan = await BuildPlanAsync(selected, cts.Token);
                if (!plan.IsEmpty)
                {
                    ProgressText = "Waiting for confirmation…";
                    if (await _shell.ConfirmAsync("Clean up selected items?", BuildConfirmation(ToConfirmation(plan)), "Clean"))
                    {
                        IsProgressIndeterminate = false;
                        ProgressValue = 0;
                        ProgressText = "Cleaning…";
                        var progress = new Progress<CleanStatus>(s =>
                        {
                            ProgressValue = s.Percent;
                            ProgressText = s.Text;
                        });

                        var outcome = await _service.CleanAsync(plan, progress, cts.Token);
                        LastCleanSummary = new CleanSummaryViewModel(outcome);
                        cleaned = true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LastCleanSummary = CleanSummaryViewModel.Cancelled();
                cleaned = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                await _shell.ShowErrorAsync("Cleanup failed", e.Message);
                cleaned = true;
            }
            finally
            {
                IsCleaning = false;
                IsProgressIndeterminate = false;
                EndOperation();
            }
        }

        if (cleaned)
            CleanFinished?.Invoke(this, EventArgs.Empty);

        await ScanAsync();
    }

    /// <summary>One-click cleanup of everything marked Safe (used by the Home page).</summary>
    [RelayCommand(CanExecute = nameof(CanCleanRecommended))]
    private async Task CleanRecommendedAsync()
    {
        SelectRecommended();
        await CleanAsync();
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectRecommended() => SetSelection(i => i.IsRecommended);

    [RelayCommand]
    private void SelectAll() => SetSelection(_ => true);

    [RelayCommand]
    private void SelectNone() => SetSelection(_ => false);

    [RelayCommand]
    private void OpenFolder(string? path)
    {
        if (!string.IsNullOrEmpty(path))
            _shell.OpenInExplorer(path);
    }

    [RelayCommand]
    private void OpenLog()
    {
        if (LastCleanSummary?.LogPath is { } log)
            _shell.OpenInExplorer(log);
    }

    [RelayCommand]
    private void DismissSummary() => LastCleanSummary = null;

    internal static string BuildConfirmation(IReadOnlyList<ConfirmationEntry> entries)
    {
        var bytes = entries.Sum(e => e.Bytes);
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"About {ByteSize.Format(bytes)} will be freed by cleaning {entries.Count} item{(entries.Count == 1 ? "" : "s")}.")
            .AppendLine().AppendLine()
            .Append("Caches and temporary files are deleted permanently; programs rebuild them when needed.");

        var recycled = entries.Where(e => e.ToRecycleBin).ToArray();
        if (recycled.Length > 0)
        {
            text.AppendLine().AppendLine()
                .Append(CultureInfo.InvariantCulture, $"Moved to the Recycle Bin: {string.Join(", ", recycled.Select(e => e.Name))}.");
        }

        var sideEffects = entries.Where(e => e.Risk != RiskLevel.Safe).ToArray();
        if (sideEffects.Length > 0)
        {
            text.AppendLine().AppendLine().Append("Items with side effects:");
            foreach (var e in sideEffects)
                text.AppendLine().Append(CultureInfo.InvariantCulture, $"• {e.Name} — {e.Description}");
        }

        if (entries.Any(e => e.IsSystemTool))
        {
            text.AppendLine().AppendLine()
                .Append("Windows cleanup tools are included; they can take several minutes. Do not turn off the computer.");
        }

        var running = entries.SelectMany(e => e.RunningProcesses).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (running.Length > 0)
        {
            text.AppendLine().AppendLine()
                .Append(CultureInfo.InvariantCulture, $"Running apps: {string.Join(", ", running)}. Close them for a complete cleanup — files they use will be skipped.");
        }

        return text.ToString();
    }

    private static IReadOnlyList<ConfirmationEntry> ToConfirmation(CleanPlan plan) =>
    [
        .. plan.Rules.Select(r => new ConfirmationEntry(
            r.Rule.Name, r.Rule.Description, r.Rule.Risk, r.TotalBytes, r.Rule.DeleteMode == DeleteMode.RecycleBin, false, r.RunningProcesses)),
        .. plan.SystemCleaners.Select(s => new ConfirmationEntry(
            s.Cleaner.Info.Name, s.Cleaner.Info.Description, s.Cleaner.Info.Risk, s.EstimatedBytes, false, true, [])),
    ];

    /// <summary>Re-scans the selected rules (the plan reflects current files and running apps) and adds selected system tools.</summary>
    private async Task<CleanPlan> BuildPlanAsync(IReadOnlyList<CleanupItemViewModel> selected, CancellationToken cancellationToken)
    {
        var rules = selected.OfType<RuleItemViewModel>().Select(r => r.Rule).ToArray();
        var freshRules = rules.Length == 0 ? [] : await _service.ScanAsync(rules, null, cancellationToken);
        var systemCleaners = selected.OfType<SystemItemViewModel>()
            .Select(s => new SystemCleanerSelection(s.Cleaner, s.TotalBytes))
            .ToArray();

        return new CleanPlan(freshRules.Where(r => r.FileCount > 0).ToArray(), systemCleaners);
    }

    private static async Task<IReadOnlyList<(ISystemCleaner Cleaner, SystemScanResult Result)>> ScanSystemCleanersAsync(
        IEnumerable<ISystemCleaner> cleaners, CancellationToken cancellationToken)
    {
        // Windows cleanup handlers are scanned one at a time, like Disk Cleanup does.
        var results = new List<(ISystemCleaner, SystemScanResult)>();
        foreach (var cleaner in cleaners)
            results.Add((cleaner, await ScanSystemCleanerAsync(cleaner, cancellationToken)));
        return results;
    }

    private static async Task<SystemScanResult> ScanSystemCleanerAsync(ISystemCleaner cleaner, CancellationToken cancellationToken)
    {
        try
        {
            return await cleaner.ScanAsync(cancellationToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Runtime.InteropServices.COMException)
        {
            return SystemScanResult.NotApplicable($"{cleaner.Info.Name}: {e.Message}");
        }
    }

    private async Task AnalyzeInBackgroundAsync(IReadOnlyList<ISystemCleaner> cleaners)
    {
        using var cts = new CancellationTokenSource();
        _analysisCts = cts;
        IsAnalyzingSystem = true;

        try
        {
            foreach (var cleaner in cleaners)
            {
                AnalysisText = $"Analyzing: {cleaner.Info.Name}. This can take a few minutes…";
                var result = await ScanSystemCleanerAsync(cleaner, cts.Token);
                if (result.IsApplicable && result.Bytes > 0)
                    AddItem(new SystemItemViewModel(cleaner, result, OnSelectionChanged));
            }
        }
        catch (OperationCanceledException)
        {
            // A new scan or a cleanup started.
        }
        finally
        {
            if (ReferenceEquals(_analysisCts, cts))
            {
                _analysisCts = null;
                IsAnalyzingSystem = false;
                AnalysisText = string.Empty;
                OnPropertyChanged(nameof(HasNothingToClean));
            }
        }
    }

    private void CancelAnalysis()
    {
        _analysisCts?.Cancel();
        _analysisCts = null;
        IsAnalyzingSystem = false;
        AnalysisText = string.Empty;
    }

    private void ApplyResults(
        IReadOnlyList<RuleScanResult> ruleResults,
        IReadOnlyList<(ISystemCleaner Cleaner, SystemScanResult Result)> systemResults)
    {
        // Keep the user's choices across re-scans; new items get their default selection.
        var previous = AllItems.ToDictionary(i => i.Id, i => i.IsSelected, StringComparer.OrdinalIgnoreCase);

        var items = ruleResults
            .Where(r => r.TotalBytes > 0)
            .Select(r => (CleanupItemViewModel)new RuleItemViewModel(r, OnSelectionChanged))
            .Concat(systemResults
                .Where(s => s.Result.IsApplicable && s.Result.Bytes > 0)
                .Select(s => new SystemItemViewModel(s.Cleaner, s.Result, OnSelectionChanged)))
            .ToArray();

        foreach (var item in items)
        {
            if (previous.TryGetValue(item.Id, out var wasSelected))
                item.IsSelected = wasSelected;
        }

        Groups.Clear();
        foreach (var group in items.GroupBy(i => i.Category).OrderBy(g => g.Key))
            Groups.Add(new CategoryGroupViewModel(group.Key, group));

        _previousSelection = previous;
        UpdateTotals();
    }

    private Dictionary<string, bool> _previousSelection = [];

    private void AddItem(CleanupItemViewModel item)
    {
        if (_previousSelection.TryGetValue(item.Id, out var wasSelected))
            item.IsSelected = wasSelected;

        var group = Groups.FirstOrDefault(g => g.Category == item.Category);
        if (group is null)
        {
            group = new CategoryGroupViewModel(item.Category, [item]);
            var index = 0;
            while (index < Groups.Count && Groups[index].Category < item.Category)
                index++;
            Groups.Insert(index, group);
        }
        else
        {
            group.Insert(item);
        }

        UpdateTotals();
    }

    private void SetSelection(Func<CleanupItemViewModel, bool> predicate)
    {
        foreach (var item in AllItems)
            item.IsSelected = predicate(item);
    }

    private void OnSelectionChanged() => UpdateSelection();

    private void UpdateTotals()
    {
        TotalBytes = AllItems.Sum(i => i.TotalBytes);
        RecommendedBytes = AllItems.Where(i => i.IsRecommended).Sum(i => i.TotalBytes);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = AllItems.Where(i => i.IsSelected).ToArray();
        SelectedBytes = selected.Sum(i => i.TotalBytes);
        SelectedCount = selected.Length;
        foreach (var group in Groups)
            group.RaiseSelectionChanged();
    }

    private CancellationTokenSource BeginOperation()
    {
        _cts = new CancellationTokenSource();
        return _cts;
    }

    private void EndOperation() => _cts = null;
}
