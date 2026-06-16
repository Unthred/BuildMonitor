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
        "diagnostics"
    ];

    public static readonly HashSet<string> DefaultSegmentSet =
        new(DefaultSegments, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Parse(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? DefaultSegments
            : configured.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
