using System.Text.Json;
using System.Text.Json.Serialization;
using COptimizer.Core.Environment;
using COptimizer.Core.Model;
using COptimizer.Core.Safety;

namespace COptimizer.Core.Rules;

/// <summary>Loads and validates rule sets from JSON.</summary>
public static class RuleSetLoader
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static IReadOnlyList<RuleDefinition> Parse(string json, string sourceName)
    {
        RuleSetDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RuleSetDocument>(json, Options);
        }
        catch (JsonException e)
        {
            throw new RuleValidationException(sourceName, [$"Invalid JSON: {e.Message}"]);
        }

        if (document is null)
            throw new RuleValidationException(sourceName, ["Document is empty."]);

        if (document.SchemaVersion != SupportedSchemaVersion)
            throw new RuleValidationException(sourceName, [$"Unsupported schemaVersion {document.SchemaVersion}."]);

        var errors = document.Rules.SelectMany(Validate).ToList();
        if (errors.Count > 0)
            throw new RuleValidationException(sourceName, errors);

        return document.Rules;
    }

    /// <summary>Merges rule sets and rejects duplicate ids across them.</summary>
    public static IReadOnlyList<RuleDefinition> Merge(IEnumerable<IReadOnlyList<RuleDefinition>> sets)
    {
        var all = sets.SelectMany(s => s).ToList();
        var duplicates = all.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"Duplicate rule id '{g.Key}'.")
            .ToList();

        if (duplicates.Count > 0)
            throw new RuleValidationException("rule sets", duplicates);

        return all;
    }

    public static IEnumerable<string> Validate(RuleDefinition rule)
    {
        var id = string.IsNullOrWhiteSpace(rule.Id) ? "<no id>" : rule.Id;

        if (string.IsNullOrWhiteSpace(rule.Id))
            yield return "Rule without id.";
        if (string.IsNullOrWhiteSpace(rule.Name))
            yield return $"{id}: name is required.";
        if (rule.Targets.Count == 0)
            yield return $"{id}: at least one target is required.";
        if (rule.MinAgeHours < 0)
            yield return $"{id}: minAgeHours must not be negative.";
        if (rule.StopServices.Any(string.IsNullOrWhiteSpace))
            yield return $"{id}: stopServices contains an empty name.";

        foreach (var target in rule.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                yield return $"{id}: target path is empty.";
                continue;
            }

            foreach (var unknown in PathVariables.FindUnknownVariables(target.Path))
                yield return $"{id}: unknown variable %{unknown}% in '{target.Path}'.";

            if (PathVariables.UsesUserVariables(target.Path) && rule.Scope != RuleScope.PerUser)
                yield return $"{id}: '{target.Path}' uses per-user variables, so scope must be PerUser.";

            if (target.Path.Split('\\', '/').Any(s => s is ".."))
                yield return $"{id}: '..' is not allowed in '{target.Path}'.";

            if (!target.Path.StartsWith('%') && !PathUtil.IsLocalDrivePath(target.Path))
                yield return $"{id}: '{target.Path}' must start with a variable or a drive letter.";

            if (string.IsNullOrWhiteSpace(target.Mask) || target.Mask.IndexOfAny(['\\', '/']) >= 0)
                yield return $"{id}: mask '{target.Mask}' must be a file name pattern.";
        }
    }

    private sealed record RuleSetDocument
    {
        public int SchemaVersion { get; init; }

        public List<RuleDefinition> Rules { get; init; } = [];
    }
}

public sealed class RuleValidationException(string source, IReadOnlyList<string> errors)
    : Exception($"Rule set '{source}' is invalid:{System.Environment.NewLine}{string.Join(System.Environment.NewLine, errors)}")
{
    public string RuleSetName { get; } = source;

    public IReadOnlyList<string> Errors { get; } = errors;
}
