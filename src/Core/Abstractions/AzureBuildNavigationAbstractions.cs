using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Abstractions;

public enum AzureBuildTimelineOutcome
{
    Ok = 0,
    PatMissing = 1,
    AuthRequired = 2,
    Unavailable = 3
}

public sealed record AzureBuildTimelineResult(
    AzureBuildTimelineOutcome Outcome,
    IReadOnlyList<AzureBuildTimelineRecord> Records,
    string? Message = null);

/// <summary>Fetches a build timeline on demand (never during normal Azure polling).</summary>
public interface IAzureBuildTimelineClient
{
    Task<AzureBuildTimelineResult> GetTimelineAsync(
        string organizationUrl,
        string adoProjectIdOrName,
        long buildId,
        string? pat,
        CancellationToken cancellationToken);
}

public sealed record AzureFailureNavigationResult(Uri DestinationUri, bool UsedTimeline);

/// <summary>Lazy failure-detail resolver with in-memory cache (navigation-driven only).</summary>
public interface IAzureFailureNavigationResolver
{
    Task<Uri> ResolveAsync(
        AzureBuildFailureNavigationRequest request,
        CancellationToken cancellationToken);

    bool TryGetCached(AzureBuildFailureNavigationRequest request, out Uri? destination);
}

/// <summary>Opens Azure navigation URIs in the default browser (future #96 hook point).</summary>
public interface IBuildSourceLinkOpener
{
    void OpenUri(Uri uri);

    Task OpenFailureDetailsAsync(
        AzureBuildFailureNavigationRequest request,
        CancellationToken cancellationToken = default);
}
