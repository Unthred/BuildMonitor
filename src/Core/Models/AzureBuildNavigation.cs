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

/// <summary>One timeline record used for failure deep links and active-run stage/job projection.</summary>
public sealed record AzureBuildTimelineRecord(
    Guid Id,
    Guid? ParentId,
    string Type,
    string? Result,
    string? Name,
    string? State = null,
    int? Order = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? FinishedAtUtc = null);

/// <summary>Structured stage row from a Builds timeline (authoritative names/state only).</summary>
public sealed record AzureTimelineStageInfo(
    Guid Id,
    string Name,
    string? State,
    string? Result,
    int? Order,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);

/// <summary>Structured job row with parent stage association.</summary>
public sealed record AzureTimelineJobInfo(
    Guid Id,
    Guid? StageId,
    string Name,
    string? StageName,
    string? State,
    string? Result,
    int? Order,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);

/// <summary>
/// Authoritative timeline-derived execution facts for one primary run.
/// Presentation policy lives in <c>AzureRunExecutionProjector</c>, not here.
/// </summary>
public sealed record AzureRunExecutionDetail(
    long RunId,
    int? TimelineChangeId,
    IReadOnlyList<AzureTimelineStageInfo> Stages,
    IReadOnlyList<AzureTimelineJobInfo> Jobs);
