using COptimizer.App.Services;
using COptimizer.Core;

namespace COptimizer.App.ViewModels;

/// <summary>Result banner shown after a cleanup.</summary>
public sealed record CleanSummaryViewModel(string Title, string Message, bool IsSuccess, string? LogPath)
{
    public CleanSummaryViewModel(CleanOutcome outcome)
        : this(
            $"Freed {ByteSize.Format(outcome.FreedBytes)}",
            BuildMessage(outcome),
            IsSuccess: !outcome.Problems.Any(),
            outcome.LogPath)
    {
    }

    public bool HasLog => LogPath is not null;

    public static CleanSummaryViewModel Cancelled() =>
        new("Cleanup cancelled", "Items cleaned before cancelling stay cleaned. The list was refreshed.", IsSuccess: false, LogPath: null);

    private static string BuildMessage(CleanOutcome outcome)
    {
        var r = outcome.Report;
        var parts = new List<string>();

        if (r.RemovedFiles > 0)
            parts.Add($"Removed {r.RemovedFiles:N0} files.");

        var tools = outcome.SystemResults.Count(s => s.Result.Succeeded);
        if (tools > 0)
            parts.Add($"{tools} Windows cleanup tool{(tools == 1 ? "" : "s")} finished.");

        if (r.SkippedFiles > 0)
        {
            var skippedBytes = r.Rules.Sum(x => x.SkippedBytes);
            parts.Add($"{r.SkippedFiles:N0} files ({ByteSize.Format(skippedBytes)}) were skipped because they are in use or changed.");
        }

        parts.AddRange(outcome.Problems);
        return parts.Count == 0 ? "Nothing needed to be removed." : string.Join(" ", parts);
    }
}
