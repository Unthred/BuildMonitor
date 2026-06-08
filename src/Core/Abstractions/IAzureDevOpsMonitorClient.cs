using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Abstractions;

public interface IAzureDevOpsMonitorClient
{
    Task<MonitorSnapshot> GetSnapshotAsync(
        AzureDevOpsSettings settings,
        CancellationToken cancellationToken);
}
