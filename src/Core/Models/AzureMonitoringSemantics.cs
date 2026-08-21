namespace BuildMonitor.Core.Models;

/// <summary>
/// CI outcome for an Azure attachment that has pipelines selected.
/// Distinct from <see cref="AzureMonitoringAvailability"/>.
/// </summary>
public enum AzureCiMonitoringState
{
    /// <summary>Zero pipelines selected — Connected / Not monitored; contributes nothing to tray health.</summary>
    NotMonitored = 0,
    Healthy = 1,
    Activity = 2,
    Warning = 3,
    Failed = 4
}

/// <summary>
/// Whether BuildMonitor can observe Azure CI for a monitored attachment.
/// Auth/network problems must not be represented as <see cref="AzureCiMonitoringState.Failed"/>.
/// </summary>
public enum AzureMonitoringAvailability
{
    Available = 0,
    AuthRequired = 1,
    Unavailable = 2
}
