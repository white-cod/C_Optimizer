using System.Text.RegularExpressions;

namespace COptimizer.Core.Environment;

/// <summary>
/// Expands %VARIABLE% tokens in rule paths. Only a fixed, known set of variables is supported, so rules
/// behave identically on every machine and an unknown token never silently expands to something unexpected.
/// </summary>
public sealed partial class PathVariables
{
    public static IReadOnlySet<string> MachineVariableNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEMDRIVE", "WINDIR", "SYSTEMROOT", "PROGRAMDATA", "PROGRAMFILES", "PROGRAMFILES(X86)",
    };

    public static IReadOnlySet<string> UserVariableNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "USERPROFILE", "LOCALAPPDATA", "APPDATA", "TEMP", "TMP", "USERNAME",
    };

    private readonly Dictionary<string, string> _values;

    private PathVariables(Dictionary<string, string> values) => _values = values;

    public static PathVariables ForMachine(SystemPaths system) => new(MachineValues(system));

    public static PathVariables ForUser(SystemPaths system, UserProfile profile)
    {
        var values = MachineValues(system);
        values["USERPROFILE"] = profile.ProfilePath;
        values["LOCALAPPDATA"] = profile.LocalAppData;
        values["APPDATA"] = profile.RoamingAppData;
        values["TEMP"] = Path.Combine(profile.LocalAppData, "Temp");
        values["TMP"] = values["TEMP"];
        values["USERNAME"] = profile.UserName;
        return new PathVariables(values);
    }

    /// <summary>Returns true when the path uses at least one per-user variable.</summary>
    public static bool UsesUserVariables(string path) =>
        VariableRegex().Matches(path).Any(m => UserVariableNames.Contains(m.Groups[1].Value));

    /// <summary>Returns the variable names used in the path that are not supported.</summary>
    public static IReadOnlyList<string> FindUnknownVariables(string path) =>
        VariableRegex().Matches(path)
            .Select(m => m.Groups[1].Value)
            .Where(v => !MachineVariableNames.Contains(v) && !UserVariableNames.Contains(v))
            .ToArray();

    /// <summary>Expands all variables. Throws when the path references a variable unavailable in this context.</summary>
    public string Expand(string path) =>
        VariableRegex().Replace(path, m =>
            _values.TryGetValue(m.Groups[1].Value, out var value)
                ? value.TrimEnd('\\', '/')
                : throw new InvalidOperationException($"Variable %{m.Groups[1].Value}% is not available in this context."));

    private static Dictionary<string, string> MachineValues(SystemPaths s) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["SYSTEMDRIVE"] = s.SystemDrive.TrimEnd('\\', '/'),
        ["WINDIR"] = s.WindowsDirectory,
        ["SYSTEMROOT"] = s.WindowsDirectory,
        ["PROGRAMDATA"] = s.ProgramData,
        ["PROGRAMFILES"] = s.ProgramFiles,
        ["PROGRAMFILES(X86)"] = s.ProgramFilesX86,
    };

    [GeneratedRegex(@"%([A-Za-z0-9_()]+)%")]
    private static partial Regex VariableRegex();
}
