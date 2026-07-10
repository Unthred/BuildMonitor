namespace BuildMonitor.Infrastructure.LocalBuild;

public static class WatchExcludeSegments
{
    public static readonly string[] DefaultSegments =
    [
        ".cursor",
        "agent-transcripts",
        "terminals",
        "mcps",
        ".specstory",
        "plans",
        ".idea",
        ".vscode",
        "bin",
        "obj",
        ".git",
        ".vs",
        "node_modules",
        "TestResults",
        "coverage",
        "logs",
        "diagnostics",
        "docs",
        "templates",
        ".github"
    ];

    public static readonly HashSet<string> DefaultSegmentSet =
        new(DefaultSegments, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Parse(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? DefaultSegments
            : configured.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static HashSet<string> ResolveIgnoreSegmentSet(
        string? configured,
        IEnumerable<string>? learnedSegments = null)
    {
        var set = new HashSet<string>(DefaultSegmentSet, StringComparer.OrdinalIgnoreCase);
        foreach (var segment in Parse(configured))
        {
            if (!string.IsNullOrWhiteSpace(segment))
            {
                set.Add(segment);
            }
        }

        if (learnedSegments is null)
        {
            return set;
        }

        foreach (var segment in learnedSegments)
        {
            if (!string.IsNullOrWhiteSpace(segment))
            {
                set.Add(segment);
            }
        }

        return set;
    }
}
