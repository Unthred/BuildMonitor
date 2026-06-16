namespace BuildMonitor.Infrastructure.LocalBuild;

public static class WatchIgnoreRules
{
    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log",
        ".jsonl",
        ".tmp",
        ".temp",
        ".cache",
        ".pdb",
        ".dll",
        ".exe",
        ".wasm",
        ".br",
        ".gz",
        ".zip",
        ".nupkg",
        ".user",
        ".suo",
        ".db",
        ".sqlite",
        ".bak",
        ".swp",
        ".mdb",
        ".meta"
    };

    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db",
        ".DS_Store",
        "desktop.ini"
    };

    public static bool IsExcludedSegment(string path, IEnumerable<string> ignoreSegments)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var segmentSet = ignoreSegments as IReadOnlySet<string>
            ?? new HashSet<string>(ignoreSegments, StringComparer.OrdinalIgnoreCase);
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => segmentSet.Contains(p));
    }

    public static bool IsNoiseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (IgnoredFileNames.Contains(fileName))
        {
            return true;
        }

        if (fileName.StartsWith('~') || fileName.EndsWith("~", StringComparison.Ordinal))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        if (extension.Length > 0 && IgnoredExtensions.Contains(extension))
        {
            return true;
        }

        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldIgnorePath(string path, IEnumerable<string> ignoreSegments) =>
        IsExcludedSegment(path, ignoreSegments) || IsNoiseFile(path);

    public static IReadOnlyList<string> FilterMeaningfulPaths(
        IEnumerable<string> paths,
        IEnumerable<string> ignoreSegments) =>
        paths.Where(p => !ShouldIgnorePath(p, ignoreSegments)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
