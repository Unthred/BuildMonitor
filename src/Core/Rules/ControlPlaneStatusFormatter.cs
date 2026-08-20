using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Formats control-plane state for the hover status panel project cards.</summary>
public static class ControlPlaneStatusFormatter
{
    private static readonly TimeSpan IdleTransitionWindow = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ShipCheckResultWindow = TimeSpan.FromMinutes(2);

    public sealed record Presentation(
        bool ShowControlPlaneSection,
        string? AgentPrimary,
        string? AgentSecondary,
        string? ChangesPrimary,
        string? ChangesSecondary,
        string? BuildActivityOverride,
        string? TransientAction,
        StatusPanelRowEmphasis AgentEmphasis = StatusPanelRowEmphasis.Normal);

    public static Presentation Format(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow)
    {
        var controlPlane = snapshot.ControlPlane ?? ProjectControlPlaneSnapshot.Unused;
        if (!ShouldShowControlPlaneSection(controlPlane))
        {
            return Hidden;
        }

        if (controlPlane.AgentTestsInProgress)
        {
            return new Presentation(
                true,
                AgentPrimary: "Idle",
                AgentSecondary: "Build allowed",
                ChangesPrimary: FormatQueuedChanges(controlPlane),
                ChangesSecondary: null,
                BuildActivityOverride: "Tests",
                TransientAction: "Running tests…",
                AgentEmphasis: StatusPanelRowEmphasis.Normal);
        }

        if (controlPlane.AgentRebuildInProgress
            || controlPlane.AgentRebuildPhase != ControlPlaneShipCheckPhase.None)
        {
            return FormatRebuildActive(controlPlane);
        }

        if (controlPlane.ShipCheckPhase != ControlPlaneShipCheckPhase.None
            || controlPlane.ShipCheckInProgress)
        {
            return FormatShipCheckActive(controlPlane);
        }

        if (controlPlane.LastAgentTestsOutcome != ControlPlaneShipCheckOutcome.None
            && controlPlane.LastAgentTestsCompletedUtc is { } testsCompleted
            && utcNow - testsCompleted <= ShipCheckResultWindow)
        {
            var passed = controlPlane.LastAgentTestsOutcome == ControlPlaneShipCheckOutcome.Passed;
            return new Presentation(
                true,
                "Idle",
                "Build allowed",
                FormatQueuedChanges(controlPlane),
                null,
                BuildActivityOverride: passed ? "Tests passed" : "Tests failed",
                TransientAction: null,
                AgentEmphasis: StatusPanelRowEmphasis.Normal);
        }

        if (controlPlane.LastAgentRebuildOutcome != ControlPlaneShipCheckOutcome.None
            && controlPlane.LastAgentRebuildCompletedUtc is { } rebuildCompleted
            && utcNow - rebuildCompleted <= ShipCheckResultWindow)
        {
            var passed = controlPlane.LastAgentRebuildOutcome == ControlPlaneShipCheckOutcome.Passed;
            return new Presentation(
                true,
                "Idle",
                "Build allowed",
                FormatQueuedChanges(controlPlane),
                null,
                BuildActivityOverride: passed ? "Rebuild passed" : "Rebuild failed",
                TransientAction: null);
        }

        if (controlPlane.LastShipCheckOutcome != ControlPlaneShipCheckOutcome.None
            && controlPlane.LastShipCheckCompletedUtc is { } completed
            && utcNow - completed <= ShipCheckResultWindow)
        {
            var passed = controlPlane.LastShipCheckOutcome == ControlPlaneShipCheckOutcome.Passed;
            var busy = controlPlane.EffectiveSessionState == ControlPlaneSessionState.Busy;
            return new Presentation(
                true,
                busy ? "Busy" : "Idle",
                busy ? FormatBusySecondary(controlPlane, utcNow) : "Build allowed",
                FormatQueuedChanges(controlPlane),
                null,
                BuildActivityOverride: passed ? "Ship check passed" : "Ship check failed",
                TransientAction: null,
                AgentEmphasis: busy ? StatusPanelRowEmphasis.Busy : StatusPanelRowEmphasis.Normal);
        }

        if (controlPlane.EffectiveSessionState == ControlPlaneSessionState.Busy)
        {
            return FormatBusy(controlPlane, utcNow);
        }

        return FormatIdle(controlPlane, utcNow);
    }

    public static bool ShouldShowControlPlaneSection(ProjectControlPlaneSnapshot controlPlane) =>
        controlPlane.SessionApiUsed
        || controlPlane.ShipCheckInProgress
        || controlPlane.AgentRebuildInProgress
        || controlPlane.ShipCheckPhase != ControlPlaneShipCheckPhase.None
        || controlPlane.AgentRebuildPhase != ControlPlaneShipCheckPhase.None
        || controlPlane.LastShipCheckOutcome != ControlPlaneShipCheckOutcome.None
        || controlPlane.LastAgentRebuildOutcome != ControlPlaneShipCheckOutcome.None
        || controlPlane.AgentTestsInProgress
        || controlPlane.LastAgentTestsOutcome != ControlPlaneShipCheckOutcome.None;

    private static Presentation Hidden { get; } = new(
        false, null, null, null, null, null, null);

    private static Presentation FormatRebuildActive(ProjectControlPlaneSnapshot controlPlane)
    {
        var phase = controlPlane.AgentRebuildPhase switch
        {
            ControlPlaneShipCheckPhase.Preparing => "Preparing",
            ControlPlaneShipCheckPhase.Building => "Building",
            ControlPlaneShipCheckPhase.ResumingWatch => "Resuming watch",
            _ => "Running"
        };

        return new Presentation(
            true,
            "Idle",
            "Build allowed",
            FormatQueuedChanges(controlPlane),
            null,
            BuildActivityOverride: $"Rebuild · {phase}",
            TransientAction: phase switch
            {
                "Building" => "Rebuilding…",
                "Resuming watch" => "Restarting watch host…",
                _ => null
            });
    }

    private static Presentation FormatShipCheckActive(ProjectControlPlaneSnapshot controlPlane)
    {
        var phase = controlPlane.ShipCheckPhase switch
        {
            ControlPlaneShipCheckPhase.Preparing => "Preparing",
            ControlPlaneShipCheckPhase.Building => "Building",
            ControlPlaneShipCheckPhase.Testing => "Testing",
            ControlPlaneShipCheckPhase.ResumingWatch => "Resuming watch",
            _ => "Running"
        };

        return new Presentation(
            true,
            "Idle",
            "Build allowed",
            FormatQueuedChanges(controlPlane),
            null,
            BuildActivityOverride: $"Ship check · {phase}",
            TransientAction: phase switch
            {
                "Building" => "Compiling…",
                "Testing" => "Running tests…",
                "Resuming watch" => "Restarting watch host…",
                _ => null
            });
    }

    private static Presentation FormatBusy(ProjectControlPlaneSnapshot controlPlane, DateTimeOffset utcNow) =>
        new(
            true,
            "Busy",
            FormatBusySecondary(controlPlane, utcNow),
            FormatQueuedChanges(controlPlane),
            null,
            BuildActivityOverride: null,
            TransientAction: null,
            AgentEmphasis: StatusPanelRowEmphasis.Busy);

    private static Presentation FormatIdle(ProjectControlPlaneSnapshot controlPlane, DateTimeOffset utcNow)
    {
        if (controlPlane.SessionSinceUtc is { } since
            && utcNow - since <= IdleTransitionWindow)
        {
            var secondary = controlPlane.IdleCause == ControlPlaneIdleCause.Timeout
                ? "Timed out · build allowed"
                : "Build allowed";
            return new Presentation(
                true,
                "Idle",
                secondary,
                FormatQueuedChanges(controlPlane),
                null,
                null,
                TransientAction: null);
        }

        return new Presentation(
            true,
            "Idle",
            "Build allowed",
            FormatQueuedChanges(controlPlane),
            null,
            null,
            null);
    }

    private static string FormatBusySecondary(ProjectControlPlaneSnapshot controlPlane, DateTimeOffset utcNow)
    {
        var parts = new List<string>();
        if (controlPlane.AutoBuildBlockedBySession)
        {
            parts.Add("Builds paused");
        }

        if (controlPlane.SessionSinceUtc is { } since)
        {
            parts.Add(FormatBusyDuration(utcNow - since));
        }

        return parts.Count == 0 ? "Busy" : string.Join(" · ", parts);
    }

    private static string? FormatQueuedChanges(ProjectControlPlaneSnapshot controlPlane)
    {
        if (!controlPlane.HasPendingFileChangeRebuild)
        {
            return null;
        }

        return controlPlane.PendingFileChangeCount switch
        {
            <= 0 => "Queued",
            1 => "1 queued",
            _ => $"{controlPlane.PendingFileChangeCount} queued"
        };
    }

    private static string FormatBusyDuration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(90))
        {
            return $"{(int)Math.Max(1, elapsed.TotalSeconds):0}s";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m";
        }

        return $"{(int)elapsed.TotalHours}h";
    }
}
