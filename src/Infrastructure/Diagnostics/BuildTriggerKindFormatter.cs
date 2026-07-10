using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

public static class BuildTriggerKindFormatter
{
    public static string ToLabel(BuildTriggerKind kind) =>
        kind switch
        {
            BuildTriggerKind.SessionStart => "Session start",
            BuildTriggerKind.ManualRebuild => "Manual rebuild",
            BuildTriggerKind.FileWatcher => "File watcher",
            BuildTriggerKind.FileWatcherQueued => "File watcher (queued)",
            BuildTriggerKind.RebuildAndRestart => "Rebuild & restart",
            BuildTriggerKind.HotReloadRebuild => "Hot reload rebuild",
            BuildTriggerKind.HotReloadRestart => "Hot reload restart",
            BuildTriggerKind.DotNetWatchCompile => "dotnet watch compile",
            BuildTriggerKind.DotNetWatchFileChange => "dotnet watch file change",
            BuildTriggerKind.EditActivitySample => "Edit activity sample",
            _ => "Other"
        };

    public static BuildTriggerKind FromBuildReason(string buildReason, bool triggeredByFileChange)
    {
        if (triggeredByFileChange)
        {
            return buildReason.Contains("queued", StringComparison.OrdinalIgnoreCase)
                ? BuildTriggerKind.FileWatcherQueued
                : BuildTriggerKind.FileWatcher;
        }

        return buildReason switch
        {
            "startup" => BuildTriggerKind.SessionStart,
            "manual rebuild" => BuildTriggerKind.ManualRebuild,
            "rebuild & restart" => BuildTriggerKind.RebuildAndRestart,
            "hot reload rebuild" => BuildTriggerKind.HotReloadRebuild,
            _ => BuildTriggerKind.Other
        };
    }
}
