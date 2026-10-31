using COptimizer.Core;
using COptimizer.Core.Cleaning;
using COptimizer.Core.Model;
using COptimizer.Core.Scanning;
using COptimizer.Core.SystemCleaners;
using COptimizer.Rules;
using COptimizer.Windows;

namespace COptimizer.Cli;

internal static class Program
{
    private const string Usage = """
        Usage:
          coptimizer rules                                   List built-in rules
          coptimizer scan  [--level safe|moderate|expert] [--rule <id>]... [--verbose]
          coptimizer clean [--level safe|moderate|expert] [--rule <id>]... (--dry-run | --yes)
          coptimizer system                                  Scan system cleaners (Windows.old, component store, ...)
          coptimizer system --rule <id> --yes                Run the given system cleaner(s)

        --level   Include rules up to this risk level (default: safe).
        --rule    Only the given rule(s); overrides --level.
        --dry-run Show what would be removed without removing anything.
        --yes     Actually remove files.
        """;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            var options = CliOptions.Parse(args[1..]);
            return args[0] switch
            {
                "rules" => ListRules(),
                "scan" => await ScanAsync(options).ConfigureAwait(false),
                "clean" => await CleanAsync(options).ConfigureAwait(false),
                "system" => await SystemAsync(options).ConfigureAwait(false),
                _ => Fail($"Unknown command '{args[0]}'."),
            };
        }
        catch (ArgumentException e)
        {
            return Fail(e.Message);
        }
    }

    private static int ListRules()
    {
        foreach (var group in BuiltInRules.All.GroupBy(r => r.Category))
        {
            Console.WriteLine($"[{group.Key}]");
            foreach (var r in group)
                Console.WriteLine($"  {r.Id,-36} {r.Risk,-9} {r.Name}");
        }

        return 0;
    }

    private static async Task<int> ScanAsync(CliOptions options)
    {
        var results = await RunScanAsync(options).ConfigureAwait(false);
        PrintScan(results, options.Verbose);
        return 0;
    }

    private static async Task<int> CleanAsync(CliOptions options)
    {
        if (!options.DryRun && !options.Yes)
            return Fail("Pass --dry-run to preview or --yes to remove files.");

        var context = CreateContext();
        var results = await RunScanAsync(options, context).ConfigureAwait(false);
        var plan = results.Where(r => r.FileCount > 0).ToArray();
        PrintScan(plan, verbose: false);

        var engine = new CleanEngine(context, new ShellRecycleBin(), services: new WindowsServiceManager());
        var report = await engine.CleanAsync(plan, new CleanOptions { DryRun = options.DryRun }).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(options.DryRun ? "Dry run — nothing was removed." : "Cleanup finished.");
        foreach (var rule in report.Rules)
        {
            var skipped = rule.Skipped.Count(s => s.Reason != SkipReason.Missing);
            Console.WriteLine($"  {rule.RuleName,-45} {Format(rule.RemovedBytes),10}  files: {rule.RemovedFiles}" +
                              (skipped > 0 ? $", skipped: {skipped} ({Format(rule.SkippedBytes)})" : string.Empty));
        }

        Console.WriteLine($"{(options.DryRun ? "Would free" : "Freed")}: {Format(report.RemovedBytes)} in {report.RemovedFiles} files; " +
                          $"skipped {report.SkippedFiles} files (in use, changed or protected).");
        return 0;
    }

    private static async Task<int> SystemAsync(CliOptions options)
    {
        var context = CreateContext();
        var cleaners = WindowsSystemCleaners.Create(context.System.SystemDrive);
        var isAdmin = Elevation.IsAdministrator();
        if (!isAdmin)
            Console.WriteLine("Note: not running as administrator — Windows cleanup handlers are skipped.");

        if (options.RuleIds.Count > 0)
        {
            if (!options.Yes)
                return Fail("Pass --yes to run system cleaners.");

            foreach (var id in options.RuleIds)
            {
                var cleaner = cleaners.FirstOrDefault(c => c.Info.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Unknown system cleaner '{id}'.");
                var progress = new Progress<SystemCleanProgress>(p =>
                    Console.Write($"\r  {cleaner.Info.Name}: {Format(p.FreedBytes)} of {Format(p.TotalBytes)}   "));
                var result = await cleaner.CleanAsync(progress, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine();
                Console.WriteLine(result.Succeeded
                    ? $"  {cleaner.Info.Name}: freed {Format(result.FreedBytes)}"
                    : $"  {cleaner.Info.Name}: {result.Error}");
            }

            return 0;
        }

        foreach (var cleaner in cleaners)
        {
            if (cleaner.RequiresAdministrator && !isAdmin)
                continue;

            var started = System.Diagnostics.Stopwatch.StartNew();
            var result = await cleaner.ScanAsync(CancellationToken.None).ConfigureAwait(false);
            var state = result.IsApplicable ? Format(result.Bytes) : "—";
            Console.WriteLine($"{cleaner.Info.Id,-38} {state,10}  {cleaner.Info.Risk,-9} {started.Elapsed.TotalSeconds,6:0.0}s  {cleaner.Info.Name}");
            foreach (var w in result.Warnings)
                Console.WriteLine($"    ! {w}");
        }

        return 0;
    }

    private static CleanupContext CreateContext() =>
        CleanupContext.Create(new WindowsSystemPathsProvider(), new RegistryProfileProvider());

    private static async Task<IReadOnlyList<RuleScanResult>> RunScanAsync(CliOptions options, CleanupContext? context = null)
    {
        context ??= CreateContext();
        if (!Elevation.IsAdministrator())
            Console.WriteLine("Note: not running as administrator — other users' profiles and some system folders are skipped.");

        var rules = SelectRules(options);
        var scanner = new Scanner(context, systemState: new WindowsSystemState());
        return await scanner.ScanAsync(rules).ConfigureAwait(false);
    }

    private static RuleDefinition[] SelectRules(CliOptions options)
    {
        if (options.RuleIds.Count == 0)
            return BuiltInRules.All.Where(r => r.Risk <= options.Level).ToArray();

        var unknown = options.RuleIds.Where(id => BuiltInRules.All.All(r => !r.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"Unknown rule(s): {string.Join(", ", unknown)}");

        return BuiltInRules.All.Where(r => options.RuleIds.Contains(r.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static void PrintScan(IReadOnlyList<RuleScanResult> results, bool verbose)
    {
        foreach (var r in results.Where(r => r.IsApplicable).OrderByDescending(r => r.TotalBytes))
        {
            var running = r.RunningProcesses.Count > 0 ? $"  [close: {string.Join(", ", r.RunningProcesses)}]" : string.Empty;
            Console.WriteLine($"{r.Rule.Name,-45} {Format(r.TotalBytes),10}  {r.Rule.Risk,-9} files: {r.FileCount}{running}");

            if (!verbose)
                continue;

            foreach (var t in r.Targets)
                Console.WriteLine($"    {Format(t.TotalBytes),10}  {t.Root}");
            foreach (var w in r.Warnings)
                Console.WriteLine($"    ! {w}");
        }

        var warnings = results.Sum(r => r.Warnings.Count);
        Console.WriteLine($"Total: {Format(results.Sum(r => r.TotalBytes))}" +
                          (warnings > 0 && !verbose ? $" ({warnings} warnings, use --verbose)" : string.Empty));
    }

    private static string Format(long bytes) => ByteSize.Format(bytes);

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine(Usage);
        return 1;
    }
}

internal sealed record CliOptions
{
    public RiskLevel Level { get; private init; } = RiskLevel.Safe;

    public List<string> RuleIds { get; } = [];

    public bool DryRun { get; private init; }

    public bool Yes { get; private init; }

    public bool Verbose { get; private init; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            options = args[i] switch
            {
                "--level" => options with { Level = Enum.Parse<RiskLevel>(Next(args, ref i), ignoreCase: true) },
                "--rule" => options.AddRule(Next(args, ref i)),
                "--dry-run" => options with { DryRun = true },
                "--yes" => options with { Yes = true },
                "--verbose" or "-v" => options with { Verbose = true },
                _ => throw new ArgumentException($"Unknown option '{args[i]}'."),
            };
        }

        return options;
    }

    private CliOptions AddRule(string id)
    {
        RuleIds.Add(id);
        return this;
    }

    private static string Next(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new ArgumentException($"Option '{args[i - 1]}' requires a value.");
}
