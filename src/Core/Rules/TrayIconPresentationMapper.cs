using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Maps composite project health snapshots to tray icon presentation states.
/// Precedence: Failed &gt; Building &gt; Attention &gt; Healthy &gt; Neutral.
/// Does not alter health evaluation — presentation only.
/// </summary>
public static class TrayIconPresentationMapper
{
    public static TrayIconPresentationState Resolve(IReadOnlyList<ProjectHealthSnapshot> activeSnapshots)
    {
        if (activeSnapshots.Count == 0)
        {
            return TrayIconPresentationState.Neutral;
        }

        var rollup = LocalTrayIconRollupEvaluator.Rollup(activeSnapshots);
        if (rollup == MonitorHealth.Red)
        {
            return TrayIconPresentationState.Failed;
        }

        if (activeSnapshots.Any(HasTrayBuildingActivity))
        {
            return TrayIconPresentationState.Building;
        }

        if (rollup == MonitorHealth.Amber)
        {
            return TrayIconPresentationState.Attention;
        }

        if (rollup == MonitorHealth.Green)
        {
            return TrayIconPresentationState.Healthy;
        }

        return TrayIconPresentationState.Neutral;
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
