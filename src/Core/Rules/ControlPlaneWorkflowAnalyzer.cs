using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class ControlPlaneWorkflowAnalyzer
{
    private static readonly TimeSpan BuildCorrelationWindow = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DebounceGrace = TimeSpan.FromSeconds(15);

    public static ControlPlaneWorkflowSnapshot Analyze(
        string projectId,
        ControlPlaneSessionStatus? session,
        IReadOnlyList<ControlPlaneEventRecord> events,
        IReadOnlyList<BuildTriggerRecord> buildTriggers,
        int buildsBlockedToday,
        DateTimeOffset utcNow)
    {
        var projectEvents = events
            .Where(e => e.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();

        var recentEvents = projectEvents.Take(20).ToList();
        var fileChangeBuilds = buildTriggers
            .Where(b => b.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase)
                && IsFileChangeBuild(b.Kind))
            .OrderByDescending(b => b.OccurredAtUtc)
            .ToList();

        if (session?.SessionApiUsed != true
            && !projectEvents.Any(e => e.Kind == ControlPlaneEventKind.Busy))
        {
            return new ControlPlaneWorkflowSnapshot(
                projectId,
                ControlPlaneWorkflowHealth.NoSessionApi,
                "No agent session yet",
                "No /session/busy calls recorded today — builds use debounce only.",
                "—",
                0,
                buildsBlockedToday,
                0,
                recentEvents);
        }

        if (session?.State == ControlPlaneSessionState.Busy)
        {
            var blocked = buildsBlockedToday;
            return new ControlPlaneWorkflowSnapshot(
                projectId,
                ControlPlaneWorkflowHealth.Busy,
                "Agent busy — builds held",
                blocked > 0
                    ? $"{blocked} file-change build(s) blocked while busy."
                    : "Automatic builds paused until /session/idle.",
                FormatBusyCycle(projectEvents, blocked),
                0,
                buildsBlockedToday,
                0,
                recentEvents);
        }

        var lastIdle = projectEvents.FirstOrDefault(e =>
            e.Kind is ControlPlaneEventKind.IdleAgent or ControlPlaneEventKind.IdleTimeout);
        if (lastIdle is null)
        {
            return new ControlPlaneWorkflowSnapshot(
                projectId,
                ControlPlaneWorkflowHealth.Unknown,
                "No idle recorded yet",
                "Agent session API used but no /session/idle yet today.",
                FormatBusyCycle(projectEvents, buildsBlockedToday),
                0,
                buildsBlockedToday,
                0,
                recentEvents);
        }

        var lastBusy = projectEvents
            .Where(e => e.Kind == ControlPlaneEventKind.Busy && e.OccurredAtUtc <= lastIdle.OccurredAtUtc)
            .OrderByDescending(e => e.OccurredAtUtc)
            .FirstOrDefault();

        var buildsDuringBusy = lastBusy is null
            ? 0
            : fileChangeBuilds.Count(b =>
                b.OccurredAtUtc > lastBusy.OccurredAtUtc
                && b.OccurredAtUtc < lastIdle.OccurredAtUtc);

        if (buildsDuringBusy > 0)
        {
            return new ControlPlaneWorkflowSnapshot(
                projectId,
                ControlPlaneWorkflowHealth.BuildDuringBusy,
                "Build during busy",
                $"{buildsDuringBusy} file-change build(s) started while agent was busy — gating may have failed.",
                FormatCycle(lastBusy, lastIdle, buildsDuringBusy, fileChangeBuilds, lastIdle),
                0,
                buildsBlockedToday,
                buildsDuringBusy,
                recentEvents);
        }

        var buildsAfterIdle = fileChangeBuilds.Count(b =>
            b.OccurredAtUtc >= lastIdle.OccurredAtUtc
            && b.OccurredAtUtc <= lastIdle.OccurredAtUtc + BuildCorrelationWindow);

        if (buildsAfterIdle == 0 && utcNow - lastIdle.OccurredAtUtc < DebounceGrace)
        {
            return new ControlPlaneWorkflowSnapshot(
                projectId,
                ControlPlaneWorkflowHealth.Debouncing,
                "Debouncing after idle",
                "Waiting for quiet period before auto-build starts.",
                FormatCycle(lastBusy, lastIdle, 0, fileChangeBuilds, lastIdle),
                0,
                buildsBlockedToday,
                0,
                recentEvents);
        }

        if (buildsAfterIdle > 1)
        {
            return new ControlPlaneWorkflowSnapshot(
                projectId,
                ControlPlaneWorkflowHealth.ExtraBuilds,
                "Extra builds detected",
                $"{buildsAfterIdle} file-change build(s) within {BuildCorrelationWindow.TotalMinutes:0} min of last idle — expected 0–1.",
                FormatCycle(lastBusy, lastIdle, buildsAfterIdle, fileChangeBuilds, lastIdle),
                buildsAfterIdle,
                buildsBlockedToday,
                0,
                recentEvents);
        }

        var idleLabel = lastIdle.Kind == ControlPlaneEventKind.IdleAgent ? "idle (agent)" : "idle (timeout)";
        var detail = buildsAfterIdle == 1
            ? "One auto-build after idle — expected agent workflow."
            : buildsBlockedToday > 0
                ? $"{buildsBlockedToday} build(s) were blocked during busy; none after idle yet."
                : "Idle received; no file-change build yet (no saves or build not needed).";

        return new ControlPlaneWorkflowSnapshot(
            projectId,
            ControlPlaneWorkflowHealth.Healthy,
            buildsAfterIdle == 1 ? "Healthy — one build after idle" : "Healthy — idle received",
            detail,
            FormatCycle(lastBusy, lastIdle, buildsAfterIdle, fileChangeBuilds, lastIdle, idleLabel),
            buildsAfterIdle,
            buildsBlockedToday,
            0,
            recentEvents);
    }

    private static string FormatBusyCycle(IReadOnlyList<ControlPlaneEventRecord> events, int blocked)
    {
        var lastBusy = events.FirstOrDefault(e => e.Kind == ControlPlaneEventKind.Busy);
        if (lastBusy is null)
        {
            return "—";
        }

        var elapsed = DateTimeOffset.UtcNow - lastBusy.OccurredAtUtc;
        var blockedText = blocked > 0 ? $" · {blocked} blocked" : string.Empty;
        return $"busy {FormatDuration(elapsed)}{blockedText}";
    }

    private static string FormatCycle(
        ControlPlaneEventRecord? busy,
        ControlPlaneEventRecord idle,
        int buildCount,
        IReadOnlyList<BuildTriggerRecord> builds,
        ControlPlaneEventRecord idleAnchor,
        string? idleLabel = null)
    {
        idleLabel ??= idle.Kind == ControlPlaneEventKind.IdleAgent ? "idle (agent)" : "idle (timeout)";
        var busyPart = busy is null
            ? "busy ?"
            : $"busy {FormatDuration(idle.OccurredAtUtc - busy.OccurredAtUtc)}";

        if (buildCount == 0)
        {
            return $"{busyPart} → {idleLabel} → no build yet";
        }

        var firstBuild = builds
            .Where(b => b.OccurredAtUtc >= idleAnchor.OccurredAtUtc)
            .OrderBy(b => b.OccurredAtUtc)
            .FirstOrDefault();
        var delay = firstBuild is null
            ? string.Empty
            : $" (+{FormatDuration(firstBuild.OccurredAtUtc - idleAnchor.OccurredAtUtc)})";

        var buildWord = buildCount == 1 ? "1 build" : $"{buildCount} builds";
        return $"{busyPart} → {idleLabel} → {buildWord}{delay}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return $"{Math.Max(1, (int)duration.TotalSeconds)}s";
        }

        if (duration.TotalMinutes < 60)
        {
            return $"{duration.TotalMinutes:0.#}m";
        }

        return $"{duration.TotalHours:0.#}h";
    }

    private static bool IsFileChangeBuild(BuildTriggerKind kind) =>
        kind is BuildTriggerKind.FileWatcher
            or BuildTriggerKind.FileWatcherQueued
            or BuildTriggerKind.DotNetWatchFileChange;
}
