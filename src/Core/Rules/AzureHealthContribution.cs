using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Maps Azure CI + availability into tray <see cref="MonitorHealth"/> contributions.
/// Auth/network loss is Amber when monitoring is configured; NotMonitored (zero pipelines) contributes nothing.
/// </summary>
public static class AzureHealthContribution
{
    public static MonitorHealth? ToTrayContribution(
        AzureCiMonitoringState ci,
        AzureMonitoringAvailability availability)
    {
        // Availability first: AuthRequired/Unavailable must Amber even when CiState is a placeholder NotMonitored.
        if (availability is AzureMonitoringAvailability.AuthRequired
            or AzureMonitoringAvailability.Unavailable)
        {
            return MonitorHealth.Amber;
        }

        if (ci == AzureCiMonitoringState.NotMonitored)
        {
            return null;
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
