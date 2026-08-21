using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Maps Azure CI + availability into tray <see cref="MonitorHealth"/> contributions.
/// Contract only for Slice 1; not wired into the coalescer yet.
/// </summary>
public static class AzureHealthContribution
{
    public static MonitorHealth? ToTrayContribution(
        AzureCiMonitoringState ci,
        AzureMonitoringAvailability availability)
    {
        if (ci == AzureCiMonitoringState.NotMonitored)
        {
            return null;
        }

        if (availability is AzureMonitoringAvailability.AuthRequired
            or AzureMonitoringAvailability.Unavailable)
        {
            return MonitorHealth.Amber;
        }

        return ci switch
        {
            AzureCiMonitoringState.Failed => MonitorHealth.Red,
            AzureCiMonitoringState.Activity or AzureCiMonitoringState.Warning => MonitorHealth.Amber,
            AzureCiMonitoringState.Healthy => MonitorHealth.Green,
            _ => null
        };
    }
}
