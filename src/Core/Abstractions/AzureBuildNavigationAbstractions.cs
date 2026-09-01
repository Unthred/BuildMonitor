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

public enum AzureGitRefOutcome
{
    Ok = 0,
    PatMissing = 1,
    AuthRequired = 2,
    Unavailable = 3
}

public sealed record AzureGitRefLookupResult(
    AzureGitRefOutcome Outcome,
    bool Exists,
    string? Message = null);

/// <summary>On-demand Git ref lookup for lazy branch navigation only (#100).</summary>
public interface IAzureGitRefClient
{
    Task<AzureGitRefLookupResult> BranchRefExistsAsync(
        string organizationUrl,
        string adoProjectIdOrName,
        string repositoryId,
        string branchShortName,
        string? pat,
        CancellationToken cancellationToken);
}

/// <summary>Lazy resilient Branch resolver with in-memory cache (navigation-driven only).</summary>
public interface IAzureBranchNavigationResolver
{
    Task<Uri> ResolveAsync(
        AzureBuildBranchNavigationRequest request,
        CancellationToken cancellationToken);

    bool TryGetCached(AzureBuildBranchNavigationRequest request, out Uri? destination);
}

/// <summary>Opens Azure navigation URIs using the owning project's browser preference (#96).</summary>
public interface IBuildSourceLinkOpener
{
    void OpenUri(string projectId, Uri uri);

    Task OpenFailureDetailsAsync(
        AzureBuildFailureNavigationRequest request,
        CancellationToken cancellationToken = default);

    Task OpenBranchAsync(
        AzureBuildBranchNavigationRequest request,
        CancellationToken cancellationToken = default);
}
