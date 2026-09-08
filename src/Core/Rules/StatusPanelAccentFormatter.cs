using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class StatusPanelAccentFormatter
{
    public static bool ShouldShowAccentRail(ProjectHealthSnapshot snapshot)
    {
        var controlPlane = snapshot.ControlPlane ?? ProjectControlPlaneSnapshot.Unused;
        if (controlPlane.AgentTestsInProgress
            || controlPlane.AgentRebuildInProgress
            || controlPlane.AgentRebuildPhase != ControlPlaneShipCheckPhase.None
            || controlPlane.ShipCheckPhase != ControlPlaneShipCheckPhase.None
            || controlPlane.ShipCheckInProgress)
        {
            return snapshot.IsActive;
        }

        return snapshot.IsActive
               && (snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing
                   || snapshot.IsRestarting
                   || StatusPanelBuildVisibilityEvaluator.ShouldShowSiteAwaiting(snapshot));
    }

    public static string FormatActivityLabel(ProjectHealthSnapshot snapshot) =>
        ProjectActivityBuilder.FormatRailLabel(snapshot, DateTimeOffset.UtcNow);

    public static string FormatActivityLabel(ProjectHealthSnapshot snapshot, DateTimeOffset utcNow) =>
        ProjectActivityBuilder.FormatRailLabel(snapshot, utcNow);

    public static MonitorHealth ResolveAccentHealth(ProjectHealthSnapshot snapshot)
    {
        if (snapshot.ErrorCount > 0)
        {
            return MonitorHealth.Red;
        }

        if (snapshot.WarningCount > 0)
        {
            return MonitorHealth.Amber;
        }

        return snapshot.Health;
    }
}
