using System.IO.Enumeration;
using COptimizer.Core.Safety;

namespace COptimizer.Core.Scanning;

/// <summary>
/// Turns a path pattern such as "C:\Users\John\AppData\Local\Google\Chrome\User Data\*\Cache" into the list of
/// existing directories it matches. Reparse points (junctions, symlinks, mount points) are never traversed,
/// so a pattern can only resolve to real directories under its literal prefix.
/// </summary>
public static class PathPatternExpander
{
    private static readonly EnumerationOptions DirectoryOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static IReadOnlyList<string> Expand(string pattern)
    {
        if (!PathUtil.IsLocalDrivePath(pattern))
            return [];

        var normalizedSeparators = pattern.Replace('/', '\\');
        var root = normalizedSeparators[..3];
        var segments = normalizedSeparators[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(s => s is "." or ".."))
            return [];

        var current = new List<string> { root };
        foreach (var segment in segments)
        {
            var next = new List<string>();
            var hasWildcard = segment.AsSpan().IndexOfAny('*', '?') >= 0;

            foreach (var dir in current)
            {
                if (hasWildcard)
                {
                    next.AddRange(EnumerateMatchingDirectories(dir, segment));
                }
                else
                {
                    var candidate = Path.Combine(dir, segment);
                    if (IsRealDirectory(candidate))
                        next.Add(candidate);
                }
            }

            if (next.Count == 0)
                return [];

            current = next;
        }

        return current.Select(PathUtil.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>True when the path is an existing directory that is not a reparse point.</summary>
    public static bool IsRealDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateMatchingDirectories(string parent, string segment)
    {
        IEnumerable<DirectoryInfo> children;
        try
        {
            children = new DirectoryInfo(parent).EnumerateDirectories("*", DirectoryOptions).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;

            if (FileSystemName.MatchesSimpleExpression(segment, child.Name, ignoreCase: true))
                yield return child.FullName;
        }
    }
}
