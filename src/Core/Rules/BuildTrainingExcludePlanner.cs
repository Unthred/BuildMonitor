namespace BuildMonitor.Core.Rules;

public static class BuildTrainingExcludePlanner
{
    private static readonly HashSet<string> PreferredToolingSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cursor",
        "agent-transcripts",
        "terminals",
        "mcps",
        ".specstory",
        "plans",
        ".idea",
        ".vscode",
        ".github",
        "docs",
        "diagnostics",
        "copilot",
        "templates"
    };

    private static readonly HashSet<string> ProtectedSourceRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "src",
        "lib",
        "app",
        "tests",
        "test",
        "pages",
        "components",
        "areas",
        "controllers",
        "views"
    };

    public static IReadOnlyList<string> SuggestExcludeSegments(
        IReadOnlyList<string>? changedPaths,
        IReadOnlySet<string> alreadyExcluded)
    {
        if (changedPaths is not { Count: > 0 })
        {
            return [];
        }

        var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in changedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            TryAddSegmentSuggestion(path, alreadyExcluded, suggestions);
        }

        return suggestions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void TryAddSegmentSuggestion(
        string path,
        IReadOnlySet<string> alreadyExcluded,
        HashSet<string> suggestions)
    {
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        foreach (var part in parts)
        {
            if (PreferredToolingSegments.Contains(part) && !alreadyExcluded.Contains(part))
            {
                suggestions.Add(part);
                return;
            }
        }

        if (IsCompileSourcePath(path))
        {
            return;
        }

        var top = parts[0];
        if (!alreadyExcluded.Contains(top) && !ProtectedSourceRoots.Contains(top))
        {
            suggestions.Add(top);
        }
    }

    private static bool IsCompileSourcePath(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
}
