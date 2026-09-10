using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Maps composite project health snapshots to tray health-ring presentation (#129).
/// Health from existing rollup; activity from <see cref="HasTrayBuildingActivity"/>.
/// Does not alter health evaluation — presentation only.
/// </summary>
public static class TrayIconPresentationMapper
{
    public static TrayIconPresentation Resolve(IReadOnlyList<ProjectHealthSnapshot> activeSnapshots)
    {
        if (activeSnapshots.Count == 0)
        {
            return new TrayIconPresentation(TrayHealthRing.Neutral, IsActive: false);
        }

        var rollup = LocalTrayIconRollupEvaluator.Rollup(activeSnapshots);
        var health = rollup switch
        {
            MonitorHealth.Red => TrayHealthRing.Failed,
            MonitorHealth.Amber => TrayHealthRing.Attention,
            MonitorHealth.Green => TrayHealthRing.Healthy,
            _ => TrayHealthRing.Neutral
        };

        var isActive = activeSnapshots.Any(HasTrayBuildingActivity);
        return new TrayIconPresentation(health, isActive);
    }

    /// <summary>
    /// Local build/test/restart/edit-gating busy states plus authoritative Azure CI activity
    /// (same Azure rules as <see cref="StatusPanelOverallFormatter.IsBuildingOrAzureActivity"/>).
    /// </summary>
    public static bool HasTrayBuildingActivity(ProjectHealthSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            return false;
        }

        if (snapshot.IsRestarting
            || snapshot.State is ProjectLifecycleState.Building
                or ProjectLifecycleState.Testing
                or ProjectLifecycleState.WaitingForEdits)
        {
            return true;
        }

        return StatusPanelOverallFormatter.IsBuildingOrAzureActivity(snapshot);
    }
}
