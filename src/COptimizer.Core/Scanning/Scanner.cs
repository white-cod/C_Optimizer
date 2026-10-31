using System.Collections.Concurrent;
using System.IO.Enumeration;
using COptimizer.Core.Environment;
using COptimizer.Core.Model;
using COptimizer.Core.Processes;
using COptimizer.Core.Safety;

namespace COptimizer.Core.Scanning;

/// <summary>
/// Read-only pass: resolves rule targets on this machine and collects matching files. Never modifies anything.
/// </summary>
public sealed class Scanner
{
    // Access errors are not ignored: each unreadable folder is reported as a warning instead of looking empty.
    private static readonly EnumerationOptions EntryOptions = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private readonly CleanupContext _context;
    private readonly IProcessInspector _processes;
    private readonly TimeProvider _time;
    private readonly ISystemState? _systemState;

    public Scanner(CleanupContext context, IProcessInspector? processes = null, TimeProvider? time = null, ISystemState? systemState = null)
    {
        _context = context;
        _processes = processes ?? new ProcessInspector();
        _time = time ?? TimeProvider.System;
        _systemState = systemState;
    }

    public async Task<IReadOnlyList<RuleScanResult>> ScanAsync(
        IReadOnlyList<RuleDefinition> rules,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new RuleScanResult[rules.Count];
        var completed = 0;
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(2, System.Environment.ProcessorCount / 2),
        };

        await Parallel.ForAsync(0, rules.Count, options, (i, ct) =>
        {
            results[i] = ScanRule(rules[i], ct);
            progress?.Report(new ScanProgress(Interlocked.Increment(ref completed), rules.Count, rules[i].Name));
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        return results;
    }

    public RuleScanResult ScanRule(RuleDefinition rule, CancellationToken cancellationToken = default)
    {
        if (rule.SkipWhenRebootPending && _systemState?.IsRebootPending() == true)
        {
            return new RuleScanResult
            {
                Rule = rule,
                Targets = [],
                Warnings = ["Windows is waiting for a restart to finish installing updates. Restart the computer, then scan again."],
            };
        }

        var warnings = new ConcurrentBag<string>();
        var targets = new List<TargetScanResult>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var minWriteTimeUtc = rule.MinAgeHours > 0
            ? _time.GetUtcNow().UtcDateTime.AddHours(-rule.MinAgeHours)
            : DateTime.MaxValue;

        foreach (var (variables, userName) in VariableSets(rule))
        {
            foreach (var target in rule.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string pattern;
                try
                {
                    pattern = variables.Expand(target.Path);
                }
                catch (InvalidOperationException e)
                {
                    warnings.Add(e.Message);
                    continue;
                }

                foreach (var root in PathPatternExpander.Expand(pattern))
                {
                    var check = _context.Policy.CheckTargetRoot(root);
                    if (!check.IsAllowed)
                    {
                        warnings.Add($"Skipped '{root}': {check.Reason}");
                        continue;
                    }

                    if (!seenRoots.Add(check.NormalizedPath))
                        continue;

                    var files = CollectFiles(check.NormalizedPath, target, minWriteTimeUtc, warnings, cancellationToken);
                    targets.Add(new TargetScanResult
                    {
                        Root = check.NormalizedPath,
                        Target = target,
                        UserName = userName,
                        Files = files,
                    });
                }
            }
        }

        return new RuleScanResult
        {
            Rule = rule,
            Targets = targets,
            Warnings = warnings.ToArray(),
            RunningProcesses = targets.Count > 0 && rule.CloseProcesses.Count > 0
                ? _processes.GetRunning(rule.CloseProcesses)
                : [],
        };
    }

    private IEnumerable<(PathVariables Variables, string? UserName)> VariableSets(RuleDefinition rule)
    {
        if (rule.Scope == RuleScope.Machine)
        {
            yield return (PathVariables.ForMachine(_context.System), null);
            yield break;
        }

        foreach (var profile in _context.Profiles)
            yield return (PathVariables.ForUser(_context.System, profile), profile.UserName);
    }

    private static List<FoundFile> CollectFiles(
        string root,
        RuleTarget target,
        DateTime minWriteTimeUtc,
        ConcurrentBag<string> warnings,
        CancellationToken cancellationToken)
    {
        var files = new List<FoundFile>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", EntryOptions).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add($"Access denied: '{dir}'. Run as administrator to include it.");
                continue;
            }
            catch (IOException e)
            {
                warnings.Add($"Cannot read '{dir}': {e.Message}");
                continue;
            }

            foreach (var entry in entries)
            {
                // Never follow or delete junctions, symlinks or cloud placeholders (OneDrive files are reparse points).
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                if (entry is DirectoryInfo)
                {
                    if (target.Recursive)
                        pending.Push(entry.FullName);
                    continue;
                }

                if (entry is not FileInfo file)
                    continue;

                if (!FileSystemName.MatchesSimpleExpression(target.Mask, file.Name, ignoreCase: true))
                    continue;

                if (file.LastWriteTimeUtc >= minWriteTimeUtc)
                    continue;

                files.Add(new FoundFile(file.FullName, file.Length, file.LastWriteTimeUtc));
            }
        }

        return files;
    }
}
