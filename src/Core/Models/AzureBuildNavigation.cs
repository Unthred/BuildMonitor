namespace BuildMonitor.Core.Models;

/// <summary>Semantic Azure DevOps navigation intent for one BUILDS column link.</summary>
public enum AzureBuildLinkKind
{
    None = 0,
    RunResults = 1,
    FailureDetails = 2,
    PullRequest = 3,
    Branch = 4
}

/// <summary>
/// One navigation target. Static kinds carry a precomputed absolute HTTPS URI.
/// <see cref="AzureBuildLinkKind.FailureDetails"/> and Branch resolve lazily on click.
/// </summary>
public sealed record AzureBuildLinkTarget(
    AzureBuildLinkKind Kind,
    string? Uri = null)
{
    public static AzureBuildLinkTarget None { get; } = new(AzureBuildLinkKind.None);

    public static AzureBuildLinkTarget Static(AzureBuildLinkKind kind, string uri) =>
        new(kind, uri);

    public static AzureBuildLinkTarget FailureDetails() =>
        new(AzureBuildLinkKind.FailureDetails);

    /// <summary>Lazy branch navigation (#100) — resolved on click via <see cref="AzureBuildBranchNavigationRequest"/>.</summary>
    public static AzureBuildLinkTarget ResilientBranch() =>
        new(AzureBuildLinkKind.Branch);
}

/// <summary>Per-column navigation for one Azure BUILDS row (PrimaryRun authority).</summary>
public sealed record AzureBuildSourceNavigation(
    AzureBuildLinkTarget Status,
    AzureBuildLinkTarget Run,
    AzureBuildLinkTarget BuildNumber,
    AzureBuildLinkTarget PullRequest,
    AzureBuildLinkTarget Branch,
    AzureBuildFailureNavigationRequest? FailureRequest = null,
    AzureBuildBranchNavigationRequest? BranchRequest = null);

/// <summary>Identity for lazy failure-detail resolution (navigation-only timeline fetch).</summary>
public sealed record AzureBuildFailureNavigationRequest(
    string ProjectId,
    string ConnectionId,
    string OrganizationUrl,
    string AdoProjectIdOrName,
    long RunId);

/// <summary>Identity for lazy resilient Branch navigation (#100).</summary>
public sealed record AzureBuildBranchNavigationRequest(
    string ProjectId,
    string ConnectionId,
    string OrganizationUrl,
    string AdoProjectIdOrName,
    string RepositoryId,
    string RepositoryName,
    long RunId,
    string SourceBranchRef,
    string? SourceVersion,
    int? PullRequestNumber,
    string BranchUrlFallback);

/// <summary>Settings-derived context attached to an Azure health facet for URL building.</summary>
public sealed record AzureBuildNavigationContext(
    string ProjectId,
    string ConnectionId,
    string OrganizationUrl,
    string AdoProjectIdOrName,
    string RepositoryName,
    string RepositoryId);

/// <summary>One timeline record used to pick a failed job/task deep link.</summary>
public sealed record AzureBuildTimelineRecord(
    Guid Id,
    Guid? ParentId,
    string Type,
    string? Result,
    string? Name);
