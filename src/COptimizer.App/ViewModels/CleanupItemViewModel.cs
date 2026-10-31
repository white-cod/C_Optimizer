using CommunityToolkit.Mvvm.ComponentModel;
using COptimizer.Core;
using COptimizer.Core.Model;
using COptimizer.Core.Scanning;
using COptimizer.Core.SystemCleaners;

namespace COptimizer.App.ViewModels;

/// <summary>One selectable line in the cleanup list: a file rule or a system cleanup tool.</summary>
public abstract partial class CleanupItemViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    protected CleanupItemViewModel(Action selectionChanged) => _selectionChanged = selectionChanged;

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract CleanerCategory Category { get; }

    public abstract RiskLevel Risk { get; }

    public abstract long TotalBytes { get; }

    /// <summary>Second line under the size, e.g. "1,234 files" or "Windows cleanup tool".</summary>
    public abstract string DetailText { get; }

    public virtual IReadOnlyList<TargetItemViewModel> Targets => [];

    /// <summary>Extra information lines (system tools).</summary>
    public virtual IReadOnlyList<string> Notes => [];

    public virtual IReadOnlyList<string> Warnings => [];

    public virtual IReadOnlyList<string> RunningProcesses => [];

    public string RiskText => Risk switch
    {
        RiskLevel.Safe => "Safe",
        RiskLevel.Moderate => "Side effects",
        _ => "Expert",
    };

    /// <summary>Only Safe items are pre-selected; anything with side effects must be chosen explicitly.</summary>
    public bool IsRecommended => Risk == RiskLevel.Safe;

    public string SizeText => ByteSize.Format(TotalBytes);

    public bool HasNotes => Notes.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasRunningProcesses => RunningProcesses.Count > 0;

    public string RunningProcessesText =>
        $"Close {string.Join(", ", RunningProcesses)} first — files in use will be skipped.";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}

/// <summary>A file rule (e.g. "NVIDIA shader cache").</summary>
public sealed class RuleItemViewModel : CleanupItemViewModel
{
    public RuleItemViewModel(RuleScanResult result, Action selectionChanged)
        : base(selectionChanged)
    {
        Result = result;
        Targets = result.Targets
            .Where(t => t.Files.Count > 0)
            .OrderByDescending(t => t.TotalBytes)
            .Select(t => new TargetItemViewModel(t.Root, t.UserName, t.TotalBytes, t.Files.Count))
            .ToArray();
        IsSelected = IsRecommended;
    }

    public RuleScanResult Result { get; }

    public RuleDefinition Rule => Result.Rule;

    public override string Id => Rule.Id;

    public override string Name => Rule.Name;

    public override string Description => Rule.Description;

    public override CleanerCategory Category => Rule.Category;

    public override RiskLevel Risk => Rule.Risk;

    public override long TotalBytes => Result.TotalBytes;

    public override string DetailText => Result.FileCount == 1 ? "1 file" : $"{Result.FileCount:N0} files";

    public override IReadOnlyList<TargetItemViewModel> Targets { get; }

    public override IReadOnlyList<string> Warnings => Result.Warnings;

    public override IReadOnlyList<string> RunningProcesses => Result.RunningProcesses;
}

/// <summary>A system cleanup tool (e.g. "Previous Windows installation").</summary>
public sealed class SystemItemViewModel : CleanupItemViewModel
{
    public SystemItemViewModel(ISystemCleaner cleaner, SystemScanResult result, Action selectionChanged)
        : base(selectionChanged)
    {
        Cleaner = cleaner;
        Result = result;
        IsSelected = IsRecommended;
    }

    public ISystemCleaner Cleaner { get; }

    public SystemScanResult Result { get; }

    public override string Id => Cleaner.Info.Id;

    public override string Name => Cleaner.Info.Name;

    public override string Description => Cleaner.Info.Description;

    public override CleanerCategory Category => Cleaner.Info.Category;

    public override RiskLevel Risk => Cleaner.Info.Risk;

    public override long TotalBytes => Result.Bytes;

    public override string DetailText => "Windows tool";

    public override IReadOnlyList<string> Notes => Result.Details.Count > 0
        ? Result.Details
        : ["Cleaned by the built-in Windows cleanup component, the same one Disk Cleanup and Storage Sense use."];

    public override IReadOnlyList<string> Warnings => Result.Warnings;
}

/// <summary>A concrete folder of a rule (e.g. the Chrome cache of one profile).</summary>
public sealed record TargetItemViewModel(string Path, string? UserName, long TotalBytes, int FileCount)
{
    public string SizeText => ByteSize.Format(TotalBytes);

    public string Details => UserName is null ? $"{FileCount:N0} files" : $"User {UserName} · {FileCount:N0} files";
}
