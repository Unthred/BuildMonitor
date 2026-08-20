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
        StatusPanelRowEmphasis AgentEmphasis = StatusPanelRowEmphasis.Normal,
        string? ModePrimary = null);

    public static Presentation Format(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow)
    {
        var controlPlane = snapshot.ControlPlane ?? ProjectControlPlaneSnapshot.Unused;
        var modeLabel = ProjectBuildControlModeWire.ToDisplayLabel(controlPlane.BuildControlMode);
        if (!ShouldShowControlPlaneSection(controlPlane))
        {
            return Hidden with { ModePrimary = modeLabel };
        }

        // AI Controlled with pending changes but no session API yet — surface CHANGES without AGENT prose.
        if (!controlPlane.SessionApiUsed
            && !controlPlane.AutoBuildEnabled
            && controlPlane.HasPendingFileChangeRebuild
            && !controlPlane.ShipCheckInProgress
            && !controlPlane.AgentRebuildInProgress
            && controlPlane.ShipCheckPhase == ControlPlaneShipCheckPhase.None
            && controlPlane.AgentRebuildPhase == ControlPlaneShipCheckPhase.None
            && controlPlane.LastShipCheckOutcome == ControlPlaneShipCheckOutcome.None
            && controlPlane.LastAgentRebuildOutcome == ControlPlaneShipCheckOutcome.None
            && !controlPlane.AgentTestsInProgress
            && controlPlane.LastAgentTestsOutcome == ControlPlaneShipCheckOutcome.None)
        {
            return new Presentation(
                true,
                AgentPrimary: null,
                AgentSecondary: null,
                ChangesPrimary: FormatQueuedChanges(controlPlane),
                ChangesSecondary: FormatChangesSecondary(controlPlane),
                BuildActivityOverride: null,
                TransientAction: null,
                ModePrimary: modeLabel);
        }

        if (controlPlane.AgentTestsInProgress)
        {
            return WithMode(
                new Presentation(
                    true,
                    AgentPrimary: "Idle",
                    AgentSecondary: AgentIdleSecondary(controlPlane),
                    ChangesPrimary: FormatQueuedChanges(controlPlane),
                    ChangesSecondary: FormatChangesSecondary(controlPlane),
                    BuildActivityOverride: "Tests",
                    TransientAction: "Running tests…",
                    AgentEmphasis: StatusPanelRowEmphasis.Normal),
                modeLabel);
        }

        if (controlPlane.AgentRebuildInProgress
            || controlPlane.AgentRebuildPhase != ControlPlaneShipCheckPhase.None)
        {
            return WithMode(FormatRebuildActive(controlPlane), modeLabel);
        }

        if (controlPlane.ShipCheckPhase != ControlPlaneShipCheckPhase.None
            || controlPlane.ShipCheckInProgress)
        {
            return WithMode(FormatShipCheckActive(controlPlane), modeLabel);
        }

        if (controlPlane.LastAgentTestsOutcome != ControlPlaneShipCheckOutcome.None
            && controlPlane.LastAgentTestsCompletedUtc is { } testsCompleted
            && utcNow - testsCompleted <= ShipCheckResultWindow)
        {
            var passed = controlPlane.LastAgentTestsOutcome == ControlPlaneShipCheckOutcome.Passed;
            return WithMode(
                new Presentation(
                    true,
                    "Idle",
                    AgentIdleSecondary(controlPlane),
                    FormatQueuedChanges(controlPlane),
                    FormatChangesSecondary(controlPlane),
                    BuildActivityOverride: passed ? "Tests passed" : "Tests failed",
                    TransientAction: null,
                    AgentEmphasis: StatusPanelRowEmphasis.Normal),
                modeLabel);
        }

        if (controlPlane.LastAgentRebuildOutcome != ControlPlaneShipCheckOutcome.None
            && controlPlane.LastAgentRebuildCompletedUtc is { } rebuildCompleted
            && utcNow - rebuildCompleted <= ShipCheckResultWindow)
        {
            var passed = controlPlane.LastAgentRebuildOutcome == ControlPlaneShipCheckOutcome.Passed;
            return WithMode(
                new Presentation(
                    true,
                    "Idle",
                    AgentIdleSecondary(controlPlane),
                    FormatQueuedChanges(controlPlane),
                    FormatChangesSecondary(controlPlane),
                    BuildActivityOverride: passed ? "Rebuild passed" : "Rebuild failed",
                    TransientAction: null),
                modeLabel);
        }

        if (controlPlane.LastShipCheckOutcome != ControlPlaneShipCheckOutcome.None
            && controlPlane.LastShipCheckCompletedUtc is { } completed
            && utcNow - completed <= ShipCheckResultWindow)
        {
            var passed = controlPlane.LastShipCheckOutcome == ControlPlaneShipCheckOutcome.Passed;
            var busy = controlPlane.EffectiveSessionState == ControlPlaneSessionState.Busy;
            return WithMode(
                new Presentation(
                    true,
                    busy ? "Busy" : "Idle",
                    busy ? FormatBusySecondary(controlPlane, utcNow) : AgentIdleSecondary(controlPlane),
                    FormatQueuedChanges(controlPlane),
                    FormatChangesSecondary(controlPlane),
                    BuildActivityOverride: passed ? "Ship check passed" : "Ship check failed",
                    TransientAction: null,
                    AgentEmphasis: busy ? StatusPanelRowEmphasis.Busy : StatusPanelRowEmphasis.Normal),
                modeLabel);
        }

        if (controlPlane.EffectiveSessionState == ControlPlaneSessionState.Busy)
        {
            return WithMode(FormatBusy(controlPlane, utcNow), modeLabel);
        }

        return WithMode(FormatIdle(controlPlane, utcNow), modeLabel);
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
        || controlPlane.LastAgentTestsOutcome != ControlPlaneShipCheckOutcome.None
        || (!controlPlane.AutoBuildEnabled && controlPlane.HasPendingFileChangeRebuild);

    private static Presentation Hidden { get; } = new(
        false, null, null, null, null, null, null);

    private static Presentation WithMode(Presentation presentation, string modeLabel) =>
        presentation with { ModePrimary = modeLabel };

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
            AgentIdleSecondary(controlPlane),
            FormatQueuedChanges(controlPlane),
            FormatChangesSecondary(controlPlane),
            BuildActivityOverride: $"Agent rebuild · {phase}",
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
            AgentIdleSecondary(controlPlane),
            FormatQueuedChanges(controlPlane),
            FormatChangesSecondary(controlPlane),
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
            FormatChangesSecondary(controlPlane),
            BuildActivityOverride: null,
            TransientAction: null,
            AgentEmphasis: StatusPanelRowEmphasis.Busy);

    private static Presentation FormatIdle(ProjectControlPlaneSnapshot controlPlane, DateTimeOffset utcNow)
    {
        if (controlPlane.SessionSinceUtc is { } since
            && utcNow - since <= IdleTransitionWindow)
        {
            return new Presentation(
                true,
                "Idle",
                FormatRecentIdleSecondary(controlPlane),
                FormatQueuedChanges(controlPlane),
                FormatChangesSecondary(controlPlane),
                null,
                TransientAction: null);
        }

        return new Presentation(
            true,
            "Idle",
            AgentIdleSecondary(controlPlane),
            FormatQueuedChanges(controlPlane),
            FormatChangesSecondary(controlPlane),
            null,
            null);
    }

    private static string AgentIdleSecondary(ProjectControlPlaneSnapshot controlPlane)
    {
        if (!controlPlane.AutoBuildEnabled)
        {
            return controlPlane.HasPendingFileChangeRebuild
                ? "Editing finished"
                : "Explicit build required";
        }

        return "Build allowed";
    }

    private static string FormatRecentIdleSecondary(ProjectControlPlaneSnapshot controlPlane)
    {
        if (!controlPlane.AutoBuildEnabled)
        {
            return controlPlane.IdleCause == ControlPlaneIdleCause.Timeout
                ? "Agent session ended"
                : "Editing finished";
        }

        return controlPlane.IdleCause == ControlPlaneIdleCause.Timeout
            ? "Timed out · build allowed"
            : "Build allowed";
    }

    private static string FormatBusySecondary(ProjectControlPlaneSnapshot controlPlane, DateTimeOffset utcNow)
    {
        var parts = new List<string>();
        if (!controlPlane.AutoBuildEnabled)
        {
            parts.Add("Editing");
        }
        else if (controlPlane.AutoBuildBlockedBySession)
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

        if (!controlPlane.AutoBuildEnabled)
        {
            return controlPlane.PendingFileChangeCount switch
            {
                <= 0 => "Changes detected",
                1 => "1 detected",
                _ => $"{controlPlane.PendingFileChangeCount} detected"
            };
        }

        return controlPlane.PendingFileChangeCount switch
        {
            <= 0 => "Queued",
            1 => "1 queued",
            _ => $"{controlPlane.PendingFileChangeCount} queued"
        };
    }

    private static string? FormatChangesSecondary(ProjectControlPlaneSnapshot controlPlane)
    {
        if (!controlPlane.HasPendingFileChangeRebuild)
        {
            return null;
        }

        if (!controlPlane.AutoBuildEnabled)
        {
            return controlPlane.EffectiveSessionState == ControlPlaneSessionState.Busy
                ? "Awaiting agent"
                : "Awaiting explicit build";
        }

        return null;
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
