using COptimizer.Core.Environment;
using COptimizer.Core.Model;
using COptimizer.Core.Rules;
using COptimizer.Rules;

namespace COptimizer.Core.Tests;

public sealed class RuleTests : IDisposable
{
    private readonly TestMachine _m = new();

    public void Dispose() => _m.Dispose();

    [Fact]
    public void Built_in_rules_load_and_have_unique_ids()
    {
        var rules = BuiltInRules.All;

        Assert.NotEmpty(rules);
        Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Description), $"{r.Id} has no description"));
    }

    [Fact]
    public void Every_built_in_target_passes_the_safety_policy()
    {
        foreach (var rule in BuiltInRules.All)
        {
            var vars = rule.Scope == RuleScope.PerUser
                ? PathVariables.ForUser(_m.System, _m.Alice)
                : PathVariables.ForMachine(_m.System);

            foreach (var target in rule.Targets)
            {
                // Replace wildcards with a concrete name to check the resulting folder.
                var path = vars.Expand(target.Path).Replace("*", "x").Replace("?", "x");
                var check = _m.Context.Policy.CheckTargetRoot(path);
                Assert.True(check.IsAllowed, $"{rule.Id}: '{target.Path}' -> {check.Reason}");
            }
        }
    }

    [Fact]
    public void Built_in_caches_are_permanent_and_safe_rules_have_no_age_surprises()
    {
        Assert.All(BuiltInRules.All, r => Assert.Equal(DeleteMode.Permanent, r.DeleteMode));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "targets": [ { "path": "%HOMEPATH%\\x" } ] } ] }""", "unknown variable")]
    [InlineData("""{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "scope": "Machine", "targets": [ { "path": "%LOCALAPPDATA%\\x" } ] } ] }""", "scope must be PerUser")]
    [InlineData("""{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "targets": [ { "path": "%WINDIR%\\Temp\\..\\System32" } ] } ] }""", "'..' is not allowed")]
    [InlineData("""{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "targets": [ { "path": "temp" } ] } ] }""", "must start with")]
    [InlineData("""{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "targets": [ { "path": "%WINDIR%\\Temp", "mask": "..\\*" } ] } ] }""", "file name pattern")]
    [InlineData("""{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "targets": [] } ] }""", "at least one target")]
    [InlineData("""{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "risk": "Harmless", "targets": [ { "path": "%WINDIR%\\Temp" } ] } ] }""", "Invalid JSON")]
    [InlineData("""{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "targetz": [] } ] }""", "Invalid JSON")]
    [InlineData("""{ "schemaVersion": 2, "rules": [] }""", "Unsupported schemaVersion")]
    public void Invalid_rule_sets_are_rejected(string json, string expectedError)
    {
        var e = Assert.Throws<RuleValidationException>(() => RuleSetLoader.Parse(json, "test.json"));
        Assert.Contains(e.Errors, err => err.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_ids_across_sets_are_rejected()
    {
        const string json = """{ "schemaVersion": 1, "rules": [ { "id": "a", "name": "A", "targets": [ { "path": "%WINDIR%\\Temp" } ] } ] }""";
        var set = RuleSetLoader.Parse(json, "x.json");

        Assert.Throws<RuleValidationException>(() => RuleSetLoader.Merge([set, set]));
    }

    [Fact]
    public void Defaults_are_applied()
    {
        const string json = """
            {
              // comments and trailing commas are allowed
              "schemaVersion": 1,
              "rules": [ { "id": "a", "name": "A", "targets": [ { "path": "%WINDIR%\\Temp" }, ], }, ],
            }
            """;
        var rule = Assert.Single(RuleSetLoader.Parse(json, "x.json"));

        Assert.Equal(RiskLevel.Safe, rule.Risk);
        Assert.Equal(RuleScope.Machine, rule.Scope);
        Assert.Equal(DeleteMode.Permanent, rule.DeleteMode);
        Assert.Equal("*", rule.Targets[0].Mask);
        Assert.True(rule.Targets[0].Recursive);
    }
}
