using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Abstractions;

public interface ILocalGitContextReader
{
    Task<LocalGitContext> ReadAsync(string repositoryRoot, CancellationToken cancellationToken);
}
