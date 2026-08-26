using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// User-facing Overall footer labels from composite project health.
/// Amber activity must not surface as "Warnings".
/// </summary>
public static class StatusPanelOverallFormatter
{
    public static string FormatLabel(
        MonitorHealth overallHealth,
        IReadOnlyList<ProjectHealthSnapshot> activeSnapshots)
    {
        return overallHealth switch
        {
            MonitorHealth.Red => "Needs fix",
            MonitorHealth.Green => "Healthy",
            MonitorHealth.Unknown => "Monitoring",
            MonitorHealth.Amber => FormatAmberLabel(activeSnapshots),
            _ => "Monitoring"
        };
    }

    /// <summary>Idle-rail helper when only rollup health is known (no snapshot list).</summary>
    public static string FormatLabelFromHealth(MonitorHealth health, bool webReady = false)
    {
        _ = webReady;
        return health switch
        {
            MonitorHealth.Red => "Needs fix",
            MonitorHealth.Green => "Healthy",
            MonitorHealth.Amber => "Attention",
            _ => "Monitoring"
        };
    }

    private static string FormatAmberLabel(IReadOnlyList<ProjectHealthSnapshot> active)
    {
        if (active.Any(IsAuthOrNetworkDegraded))
        {
            return "Attention";
        }

        if (active.Any(IsBuildingOrAzureActivity))
        {
            return "Building";
        }

        // Local warning counts / partial CI without active run.
        return "Attention";
    }

    public static bool IsAuthOrNetworkDegraded(ProjectHealthSnapshot snapshot) =>
        snapshot.Azure?.Availability is AzureMonitoringAvailability.AuthRequired
            or AzureMonitoringAvailability.Unavailable;

    public static bool IsBuildingOrAzureActivity(ProjectHealthSnapshot snapshot)
    {
        if (snapshot.IsRestarting
            || snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing)
        {
            return true;
        }

        var azure = snapshot.Azure;
        if (azure is null)
        {
            return false;
        }

        if (azure.CiState == AzureCiMonitoringState.Activity)
        {
            return true;
        }

        return azure.PrimaryRun is not null && AzureRunSelector.IsActive(azure.PrimaryRun.State);
    }
}
