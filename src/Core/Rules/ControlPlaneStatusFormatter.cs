using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Formats control-plane state for the hover status panel project cards.</summary>
public static class ControlPlaneStatusFormatter
{
    private static readonly TimeSpan IdleTransitionWindow = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ShipCheckResultWindow = TimeSpan.FromMinutes(2);

    public sealed record Presentation(
        string? ActivityHeadline,
        string? AgentStatusLine,
        string? DetailLine,
        bool ShowControlPlaneSection);

    public static Presentation Format(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow)
    {
        var controlPlane = snapshot.ControlPlane ?? ProjectControlPlaneSnapshot.Unused;
        if (!ShouldShowControlPlaneSection(controlPlane))
        {
            return new Presentation(null, null, null, false);
        }

        if (controlPlane.AgentTestsInProgress)
        {
            return new Presentation("Tests — running", "Agent: Tests", null, true);
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
            var headline = controlPlane.LastAgentTestsOutcome == ControlPlaneShipCheckOutcome.Passed
                ? "Tests passed"
                : "Tests failed";
            return new Presentation(headline, "Agent: Connected · Idle", null, true);
        }

        if (controlPlane.LastAgentRebuildOutcome != ControlPlaneShipCheckOutcome.None
            && controlPlane.LastAgentRebuildCompletedUtc is { } rebuildCompleted
            && utcNow - rebuildCompleted <= ShipCheckResultWindow)
        {
            return FormatRebuildResult(controlPlane);
        }

        if (controlPlane.LastShipCheckOutcome != ControlPlaneShipCheckOutcome.None
            && controlPlane.LastShipCheckCompletedUtc is { } completed
            && utcNow - completed <= ShipCheckResultWindow)
        {
            return FormatShipCheckResult(controlPlane);
        }

        if (controlPlane.EffectiveSessionState == ControlPlaneSessionState.Busy)
        {
            return FormatBusy(controlPlane, utcNow);
        }

        return FormatIdle(controlPlane, utcNow);
    }

    public static bool ShouldShowControlPlaneSection(ProjectControlPlaneSnapshot controlPlane) =>
        controlPlane.SessionApiUsed
        ||         controlPlane.ShipCheckInProgress
        || controlPlane.AgentRebuildInProgress
        || controlPlane.ShipCheckPhase != ControlPlaneShipCheckPhase.None
        || controlPlane.AgentRebuildPhase != ControlPlaneShipCheckPhase.None
        ||         controlPlane.LastShipCheckOutcome != ControlPlaneShipCheckOutcome.None
        || controlPlane.LastAgentRebuildOutcome != ControlPlaneShipCheckOutcome.None
        || controlPlane.AgentTestsInProgress
        || controlPlane.LastAgentTestsOutcome != ControlPlaneShipCheckOutcome.None;

    private static Presentation FormatRebuildActive(ProjectControlPlaneSnapshot controlPlane)
    {
        var headline = controlPlane.AgentRebuildPhase switch
        {
            ControlPlaneShipCheckPhase.Preparing => "Rebuild — preparing",
            ControlPlaneShipCheckPhase.Building => "Rebuild — building",
            ControlPlaneShipCheckPhase.ResumingWatch => "Rebuild — resuming watch",
            _ => "Rebuild — running"
        };

        return new Presentation(headline, "Agent: Rebuild", "Watch host paused for build", true);
    }

    private static Presentation FormatRebuildResult(ProjectControlPlaneSnapshot controlPlane)
    {
        var headline = controlPlane.LastAgentRebuildOutcome == ControlPlaneShipCheckOutcome.Passed
            ? "Rebuild passed"
            : "Rebuild failed";

        return new Presentation(headline, "Agent: Connected · Idle", null, true);
    }

    private static Presentation FormatShipCheckActive(ProjectControlPlaneSnapshot controlPlane)
    {
        var headline = controlPlane.ShipCheckPhase switch
        {
            ControlPlaneShipCheckPhase.Preparing => "Ship check — preparing",
            ControlPlaneShipCheckPhase.Building => "Ship check — building",
            ControlPlaneShipCheckPhase.Testing => "Ship check — testing",
            ControlPlaneShipCheckPhase.ResumingWatch => "Ship check — resuming watch",
            _ => "Ship check — running"
        };

        return new Presentation(headline, "Agent: Ship check", null, true);
    }

    private static Presentation FormatShipCheckResult(ProjectControlPlaneSnapshot controlPlane)
    {
        var headline = controlPlane.LastShipCheckOutcome == ControlPlaneShipCheckOutcome.Passed
            ? "Ship check passed"
            : "Ship check failed";

        var agentLine = controlPlane.EffectiveSessionState == ControlPlaneSessionState.Busy
            ? "Agent: Busy"
            : "Agent: Connected · Idle";

        return new Presentation(headline, agentLine, null, true);
    }

    private static Presentation FormatBusy(ProjectControlPlaneSnapshot controlPlane, DateTimeOffset utcNow)
    {
        var detailParts = new List<string> { "Agent: Busy" };

        if (controlPlane.SessionSinceUtc is { } since)
        {
            detailParts.Add(FormatBusyDuration(utcNow - since));
        }

        if (controlPlane.AutoBuildBlockedBySession)
        {
            detailParts.Add("Automatic builds held");
        }

        AppendPendingChanges(detailParts, controlPlane);

        return new Presentation(
            ActivityHeadline: "Agent editing — builds paused",
            AgentStatusLine: null,
            DetailLine: string.Join(" · ", detailParts),
            ShowControlPlaneSection: true);
    }

    private static Presentation FormatIdle(ProjectControlPlaneSnapshot controlPlane, DateTimeOffset utcNow)
    {
        if (controlPlane.SessionSinceUtc is { } since
            && utcNow - since <= IdleTransitionWindow)
        {
            var detailParts = new List<string> { "Build allowed" };
            AppendPendingChanges(detailParts, controlPlane);
            var headline = controlPlane.IdleCause == ControlPlaneIdleCause.Timeout
                ? "Agent busy timed out · build allowed"
                : "Agent finished editing · build allowed";
            var detail = controlPlane.IdleCause == ControlPlaneIdleCause.Timeout
                ? "Timeout (no idle from agent)"
                : null;
            if (detail is not null)
            {
                detailParts.Insert(0, detail);
            }

            return new Presentation(
                ActivityHeadline: headline,
                AgentStatusLine: "Agent: Connected · Idle",
                DetailLine: string.Join(" · ", detailParts),
                ShowControlPlaneSection: true);
        }

        var idleDetail = new List<string>();
        AppendPendingChanges(idleDetail, controlPlane);

        return new Presentation(
            ActivityHeadline: null,
            AgentStatusLine: "Agent: Connected · Idle",
            DetailLine: idleDetail.Count > 0 ? string.Join(" · ", idleDetail) : null,
            ShowControlPlaneSection: true);
    }

    private static void AppendPendingChanges(List<string> parts, ProjectControlPlaneSnapshot controlPlane)
    {
        if (!controlPlane.HasPendingFileChangeRebuild)
        {
            return;
        }

        parts.Add(controlPlane.PendingFileChangeCount switch
        {
            <= 0 => "File changes queued",
            1 => "1 file change queued",
            _ => $"{controlPlane.PendingFileChangeCount} file changes queued"
        });
    }

    private static string FormatBusyDuration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(90))
        {
            return $"Busy for {(int)Math.Max(1, elapsed.TotalSeconds):0}s";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"Busy for {(int)elapsed.TotalMinutes}m";
        }

        return $"Busy for {(int)elapsed.TotalHours}h";
    }
}
