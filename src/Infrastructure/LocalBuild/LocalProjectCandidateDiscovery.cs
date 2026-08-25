namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>Discovers .csproj / .sln candidates under a root for associating Local attachments.</summary>
public static class LocalProjectCandidateDiscovery
{
    private static readonly string[] ExcludedSegments =
    [
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}bin{Path.AltDirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}obj{Path.AltDirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}.git{Path.AltDirectorySeparatorChar}"
    ];

    /// <summary>Returns project/solution paths relative to <paramref name="rootFolder"/>.</summary>
    public static IReadOnlyList<string> DiscoverRelativeCandidates(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return [];
        }

        var rootFull = Path.GetFullPath(rootFolder);
        var files = Directory.EnumerateFiles(rootFull, "*.*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                return ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".sln", StringComparison.OrdinalIgnoreCase);
            })
            .Where(path => !IsExcluded(path))
            .Select(path => ToRelative(rootFull, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return files;
    }

    public static string ToRelative(string rootFolder, string absolutePath)
    {
        var relative = Path.GetRelativePath(rootFolder, absolutePath);
        return relative.StartsWith("..", StringComparison.Ordinal) ? absolutePath : relative;
    }

    private static bool IsExcluded(string path)
    {
        foreach (var segment in ExcludedSegments)
        {
            if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
