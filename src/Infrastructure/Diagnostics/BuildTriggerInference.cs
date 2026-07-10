using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

public static class BuildTriggerInference
{
    private static readonly string[] AgentToolingSegments =
    [
        ".cursor",
        "agent-transcripts",
        "terminals",
        "mcps",
        ".specstory",
        ".github",
        "copilot"
    ];

    private static readonly string[] BuildOutputSegments =
    [
        "bin",
        "obj",
        "TestResults",
        "coverage",
        ".vs"
    ];

    public static string Infer(
        BuildTriggerKind kind,
        string? detail,
        IReadOnlyList<string>? changedPaths)
    {
        if (changedPaths is { Count: > 0 })
        {
            if (ContainsPathSegment(changedPaths, AgentToolingSegments))
            {
                return "Likely Cursor/IDE or agent activity (tooling folder touched)";
            }

            if (ContainsPathSegment(changedPaths, BuildOutputSegments))
            {
                return "Likely build/test output folder — consider watch excludes";
            }

            if (changedPaths.Any(IsLikelySourceFile))
            {
                return "Likely source file edit";
            }

            return "File change outside known tooling/output folders";
        }

        return kind switch
        {
            BuildTriggerKind.SessionStart => "Project started or enabled in session",
            BuildTriggerKind.ManualRebuild => "Manual rebuild from tray or menu",
            BuildTriggerKind.FileWatcher or BuildTriggerKind.FileWatcherQueued =>
                "File watcher fired with no captured paths — tooling touch, queued rebuild, or watcher edge case",
            BuildTriggerKind.DotNetWatchFileChange =>
                "dotnet watch detected a change (BuildMonitor did not capture the path)",
            BuildTriggerKind.DotNetWatchCompile => "dotnet watch started an internal compile",
            BuildTriggerKind.HotReloadRebuild => "Hot reload output requested a full rebuild",
            BuildTriggerKind.HotReloadRestart => "Hot reload output requested an app restart",
            BuildTriggerKind.RebuildAndRestart => "Rebuild & restart from tray or status panel",
            BuildTriggerKind.EditActivitySample =>
                "Manual snapshot from status panel while reviewing edit/agent activity",
            _ => string.IsNullOrWhiteSpace(detail) ? "—" : "See Detail column"
        };
    }

    private static bool ContainsPathSegment(IReadOnlyList<string> paths, IEnumerable<string> segments)
    {
        var segmentSet = new HashSet<string>(segments, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => segmentSet.Contains(p)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLikelySourceFile(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
}
