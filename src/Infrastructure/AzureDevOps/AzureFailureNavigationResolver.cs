using System.Collections.Concurrent;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Resolves Failed/Partial Status clicks to Azure <c>view=logs</c> URLs with build-results fallback.
/// Timeline is fetched only on navigation; successful resolutions are cached in memory by run identity.
/// </summary>
public sealed class AzureFailureNavigationResolver : IAzureFailureNavigationResolver
{
    public static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(6);

    private readonly IAzureBuildTimelineClient timelineClient;
    private readonly IAzureConnectionSecretStore secretStore;
    private readonly ConcurrentDictionary<string, Uri> cache = new(StringComparer.OrdinalIgnoreCase);

    public AzureFailureNavigationResolver(
        IAzureBuildTimelineClient timelineClient,
        IAzureConnectionSecretStore secretStore)
    {
        this.timelineClient = timelineClient;
        this.secretStore = secretStore;
    }

    public bool TryGetCached(AzureBuildFailureNavigationRequest request, out Uri? destination)
    {
        if (cache.TryGetValue(BuildCacheKey(request), out var uri))
        {
            destination = uri;
            return true;
        }

        destination = null;
        return false;
    }

    public async Task<Uri> ResolveAsync(
        AzureBuildFailureNavigationRequest request,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(request);
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var fallback = BuildFallbackUri(request);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ResolveTimeout);

        try
        {
            string? pat;
            try
            {
                pat = await secretStore.LoadAsync(request.ConnectionId, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CacheAndReturn(cacheKey, fallback);
            }
            catch
            {
                return CacheAndReturn(cacheKey, fallback);
            }

            var timeline = await timelineClient.GetTimelineAsync(
                request.OrganizationUrl,
                request.AdoProjectIdOrName,
                request.RunId,
                pat,
                timeoutCts.Token).ConfigureAwait(false);

            if (timeline.Outcome != AzureBuildTimelineOutcome.Ok)
            {
                return CacheAndReturn(cacheKey, fallback);
            }

            var target = AzureBuildTimelineFailureSelector.TrySelectBestFailedLogTarget(timeline.Records);
            if (target is null)
            {
                return CacheAndReturn(cacheKey, fallback);
            }

            var url = target.TaskId is { } taskId
                ? AzureDevOpsDeepLinkBuilder.BuildRunTaskLogsUrl(
                    request.OrganizationUrl,
                    request.AdoProjectIdOrName,
                    request.RunId,
                    target.JobId,
                    taskId)
                : AzureDevOpsDeepLinkBuilder.BuildRunJobLogsUrl(
                    request.OrganizationUrl,
                    request.AdoProjectIdOrName,
                    request.RunId,
                    target.JobId);

            if (!Uri.TryCreate(url, UriKind.Absolute, out var resolved))
            {
                return CacheAndReturn(cacheKey, fallback);
            }

            return CacheAndReturn(cacheKey, resolved);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CacheAndReturn(cacheKey, fallback);
        }
        catch
        {
            return CacheAndReturn(cacheKey, fallback);
        }
    }

    private static Uri BuildFallbackUri(AzureBuildFailureNavigationRequest request)
    {
        var url = AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(
            request.OrganizationUrl,
            request.AdoProjectIdOrName,
            request.RunId);
        return new Uri(url, UriKind.Absolute);
    }

    private static string BuildCacheKey(AzureBuildFailureNavigationRequest request) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{request.ConnectionId}|{request.AdoProjectIdOrName}|{request.RunId}");

    private Uri CacheAndReturn(string cacheKey, Uri destination)
    {
        cache[cacheKey] = destination;
        return destination;
    }
}
