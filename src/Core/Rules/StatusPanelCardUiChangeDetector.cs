using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Decides whether the hover status panel must rebuild project cards.
/// Separates live health ticks from WPF control lifetime so action buttons stay clickable.
/// </summary>
public static class StatusPanelCardUiChangeDetector
{
    public static bool RequiresCardRebuild(
        IReadOnlyList<ProjectHealthSnapshot> previous,
        IReadOnlyList<ProjectHealthSnapshot> current)
    {
        var prevActive = ActiveOrdered(previous);
        var currActive = ActiveOrdered(current);
        if (prevActive.Count != currActive.Count)
        {
            return true;
        }

        for (var i = 0; i < prevActive.Count; i++)
        {
            if (!CardUiEquals(prevActive[i], currActive[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static List<ProjectHealthSnapshot> ActiveOrdered(IReadOnlyList<ProjectHealthSnapshot> snapshots) =>
        snapshots.Where(s => s.IsActive).OrderBy(s => s.ProjectId, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool CardUiEquals(ProjectHealthSnapshot a, ProjectHealthSnapshot b) =>
        string.Equals(a.ProjectId, b.ProjectId, StringComparison.OrdinalIgnoreCase)
        && a.DisplayName == b.DisplayName
        && a.Health == b.Health
        && a.HealthLabel == b.HealthLabel
        && a.State == b.State
        && a.LastExitCode == b.LastExitCode
        && a.LastDuration == b.LastDuration
        && a.LastErrorPreview == b.LastErrorPreview
        && a.ErrorCount == b.ErrorCount
        && a.WarningCount == b.WarningCount
        && a.LastBuildFinishedAtUtc == b.LastBuildFinishedAtUtc
        && a.ListenUrl == b.ListenUrl
        && a.ListenUrlReady == b.ListenUrlReady
        && a.SupportsAppRestart == b.SupportsAppRestart
        && a.IssueCountsText == b.IssueCountsText
        && a.FailurePhase == b.FailurePhase
        && a.IsRestarting == b.IsRestarting
        && a.IsEditGatingActive == b.IsEditGatingActive
        && a.EditGatingDetailText == b.EditGatingDetailText
        && a.RebuildQuietUntilUtc == b.RebuildQuietUntilUtc
        && ControlPlaneEquals(a.ControlPlane, b.ControlPlane)
        && ProgressStepsEqual(a.ProgressSteps, b.ProgressSteps);

    private static bool ControlPlaneEquals(
        ProjectControlPlaneSnapshot? left,
        ProjectControlPlaneSnapshot? right)
    {
        left ??= ProjectControlPlaneSnapshot.Unused;
        right ??= ProjectControlPlaneSnapshot.Unused;
        return left.SessionApiUsed == right.SessionApiUsed
               && left.EffectiveSessionState == right.EffectiveSessionState
               && left.SessionSinceUtc == right.SessionSinceUtc
               && left.AutoBuildBlockedBySession == right.AutoBuildBlockedBySession
               && left.HasPendingFileChangeRebuild == right.HasPendingFileChangeRebuild
               && left.PendingFileChangeCount == right.PendingFileChangeCount
               && left.ShipCheckPhase == right.ShipCheckPhase
               && left.LastShipCheckOutcome == right.LastShipCheckOutcome
               && left.LastShipCheckCompletedUtc == right.LastShipCheckCompletedUtc
               && left.ShipCheckInProgress == right.ShipCheckInProgress
               && left.AgentRebuildInProgress == right.AgentRebuildInProgress
               && left.AgentRebuildPhase == right.AgentRebuildPhase
               && left.LastAgentRebuildOutcome == right.LastAgentRebuildOutcome
               && left.LastAgentRebuildCompletedUtc == right.LastAgentRebuildCompletedUtc;
    }

    private static bool ProgressStepsEqual(
        IReadOnlyList<BuildProgressStep> left,
        IReadOnlyList<BuildProgressStep> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
