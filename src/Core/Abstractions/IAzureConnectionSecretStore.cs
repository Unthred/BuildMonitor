namespace BuildMonitor.Core.Abstractions;

/// <summary>Stores Azure DevOps PATs outside settings.json, keyed by connection id.</summary>
public interface IAzureConnectionSecretStore
{
    Task SaveAsync(string connectionId, string pat, CancellationToken cancellationToken);

    Task<string?> LoadAsync(string connectionId, CancellationToken cancellationToken);

    Task DeleteAsync(string connectionId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string connectionId, CancellationToken cancellationToken);
}
