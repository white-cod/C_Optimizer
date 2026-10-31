using System.Reflection;
using COptimizer.Core.Model;
using COptimizer.Core.Rules;

namespace COptimizer.Rules;

/// <summary>Rule sets shipped inside the executable (Definitions/*.json, embedded as resources).</summary>
public static class BuiltInRules
{
    private const string ResourcePrefix = "COptimizer.Rules.";

    private static readonly Lazy<IReadOnlyList<RuleDefinition>> Cached = new(LoadAll);

    public static IReadOnlyList<RuleDefinition> All => Cached.Value;

    private static IReadOnlyList<RuleDefinition> LoadAll()
    {
        var assembly = typeof(BuiltInRules).Assembly;
        var sets = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n => RuleSetLoader.Parse(ReadResource(assembly, n), n[ResourcePrefix.Length..]));

        return RuleSetLoader.Merge(sets);
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Resource '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
