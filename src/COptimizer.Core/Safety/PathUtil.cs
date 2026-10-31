namespace COptimizer.Core.Safety;

public static class PathUtil
{
    /// <summary>
    /// Normalizes an absolute local path: resolves "." and "..", unifies separators and removes a trailing
    /// separator (except for a drive root such as "C:\").
    /// </summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Replace('/', '\\'));
        var root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd('\\') : full;
    }

    /// <summary>True for paths like "C:\..." — rejects relative, UNC and device paths.</summary>
    public static bool IsLocalDrivePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    public static bool IsSameOrInside(string path, string parent)
    {
        if (path.Equals(parent, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = parent.EndsWith('\\') ? parent : parent + "\\";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStrictlyInside(string path, string parent) =>
        !path.Equals(parent, StringComparison.OrdinalIgnoreCase) && IsSameOrInside(path, parent);
}
