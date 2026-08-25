using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Git;

/// <summary>Caches <see cref="ILocalGitContextReader"/> results briefly so Azure poll loops do not spawn git constantly.</summary>
public sealed class CachedLocalGitContextReader(ILocalGitContextReader inner, TimeSpan? ttl = null) : ILocalGitContextReader
{
    private readonly TimeSpan ttl = ttl ?? TimeSpan.FromSeconds(15);
    private readonly object sync = new();
    private readonly Dictionary<string, (LocalGitContext Context, DateTimeOffset CachedAtUtc)> cache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<LocalGitContext> ReadAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var key = repositoryRoot?.Trim() ?? string.Empty;
        lock (sync)
        {
            if (cache.TryGetValue(key, out var hit)
                && DateTimeOffset.UtcNow - hit.CachedAtUtc < ttl)
            {
                return hit.Context;
            }
        }

        var context = await inner.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            cache[key] = (context, DateTimeOffset.UtcNow);
        }

        return context;
    }

    public void Invalidate(string? repositoryRoot = null)
    {
        lock (sync)
        {
            if (repositoryRoot is null)
            {
                cache.Clear();
                return;
            }

            cache.Remove(repositoryRoot.Trim());
        }
    }
}
