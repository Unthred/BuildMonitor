using System.Collections.Concurrent;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Resolves Branch clicks to branch, PR, commit, or build-results URLs (#100).
/// Ref existence is checked only on navigation; outcomes are cached in memory.
/// </summary>
public sealed class AzureBranchNavigationResolver : IAzureBranchNavigationResolver
{
    public static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan ExistsCacheTtl = TimeSpan.FromMinutes(2);

    private readonly IAzureGitRefClient gitRefClient;
    private readonly IAzureConnectionSecretStore secretStore;
    private readonly ConcurrentDictionary<string, CachedBranchNav> cache = new(StringComparer.OrdinalIgnoreCase);

    public AzureBranchNavigationResolver(
        IAzureGitRefClient gitRefClient,
        IAzureConnectionSecretStore secretStore)
    {
        this.gitRefClient = gitRefClient;
        this.secretStore = secretStore;
    }

    public bool TryGetCached(AzureBuildBranchNavigationRequest request, out Uri? destination)
    {
        if (TryGetFreshCacheEntry(request, out var entry))
        {
            destination = entry.Destination;
            return true;
        }

        destination = null;
        return false;
    }

    public async Task<Uri> ResolveAsync(
        AzureBuildBranchNavigationRequest request,
        CancellationToken cancellationToken)
    {
        if (TryGetFreshCacheEntry(request, out var cached))
        {
            return cached.Destination;
        }

        var urls = BuildCandidateUrls(request);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ResolveTimeout);

        AzureBranchRefExistence existence;
        try
        {
            existence = await DetermineExistenceAsync(request, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            existence = AzureBranchRefExistence.Unknown;
        }
        catch
        {
            existence = AzureBranchRefExistence.Unknown;
        }

        var destination = AzureBranchNavigationPolicy.SelectDestination(
            existence,
            urls.BranchUrl,
            urls.RunResultsUrl,
            urls.PullRequestUrl,
            urls.CommitUrl);

        if (AzureBranchNavigationPolicy.ShouldCacheOutcome(existence))
        {
            cache[BuildCacheKey(request)] = new CachedBranchNav(destination, existence, DateTimeOffset.UtcNow);
        }

        return destination;
    }

    private async Task<AzureBranchRefExistence> DetermineExistenceAsync(
        AzureBuildBranchNavigationRequest request,
        CancellationToken cancellationToken)
    {
        var shortName = AzureGitBranchNormalizer.ToShortName(request.SourceBranchRef);
        if (string.IsNullOrWhiteSpace(shortName))
        {
            return AzureBranchRefExistence.Unknown;
        }

        string? pat;
        try
        {
            pat = await secretStore.LoadAsync(request.ConnectionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return AzureBranchRefExistence.Unknown;
        }

        var lookup = await gitRefClient.BranchRefExistsAsync(
            request.OrganizationUrl,
            request.AdoProjectIdOrName,
            request.RepositoryId,
            shortName,
            pat,
            cancellationToken).ConfigureAwait(false);

        return lookup.Outcome switch
        {
            AzureGitRefOutcome.Ok when lookup.Exists => AzureBranchRefExistence.Exists,
            AzureGitRefOutcome.Ok => AzureBranchRefExistence.Deleted,
            _ => AzureBranchRefExistence.Unknown
        };
    }

    private bool TryGetFreshCacheEntry(AzureBuildBranchNavigationRequest request, out CachedBranchNav entry)
    {
        if (!cache.TryGetValue(BuildCacheKey(request), out var cached))
        {
            entry = default!;
            return false;
        }

        entry = cached;

        if (AzureBranchNavigationPolicy.IsLongLivedCache(entry.Existence))
        {
            return true;
        }

        if (entry.Existence == AzureBranchRefExistence.Exists
            && DateTimeOffset.UtcNow - entry.CachedAtUtc <= ExistsCacheTtl)
        {
            return true;
        }

        cache.TryRemove(BuildCacheKey(request), out _);
        entry = default!;
        return false;
    }

    private static CandidateUrls BuildCandidateUrls(AzureBuildBranchNavigationRequest request)
    {
        var runResultsUrl = AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(
            request.OrganizationUrl,
            request.AdoProjectIdOrName,
            request.RunId);

        string? pullRequestUrl = request.PullRequestNumber is int pr && pr > 0
            ? AzureDevOpsDeepLinkBuilder.BuildPullRequestUrl(
                request.OrganizationUrl,
                request.AdoProjectIdOrName,
                request.RepositoryName,
                pr)
            : null;

        string? commitUrl = AzureGitCommitIdValidator.IsValidCommitId(request.SourceVersion)
            ? AzureDevOpsDeepLinkBuilder.BuildCommitUrl(
                request.OrganizationUrl,
                request.AdoProjectIdOrName,
                request.RepositoryName,
                request.SourceVersion!)
            : null;

        return new CandidateUrls(request.BranchUrlFallback, runResultsUrl, pullRequestUrl, commitUrl);
    }

    private static string BuildCacheKey(AzureBuildBranchNavigationRequest request) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{request.ConnectionId}|{request.AdoProjectIdOrName}|{request.RepositoryId}|{request.RunId}|{request.SourceBranchRef}|{request.SourceVersion ?? string.Empty}");

    private sealed record CandidateUrls(
        string BranchUrl,
        string RunResultsUrl,
        string? PullRequestUrl,
        string? CommitUrl);

    private sealed record CachedBranchNav(Uri Destination, AzureBranchRefExistence Existence, DateTimeOffset CachedAtUtc);
}
