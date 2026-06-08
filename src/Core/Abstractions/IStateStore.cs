using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Abstractions;

public interface IStateStore
{
    Task<MonitorSnapshot?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(MonitorSnapshot snapshot, CancellationToken cancellationToken);
}
