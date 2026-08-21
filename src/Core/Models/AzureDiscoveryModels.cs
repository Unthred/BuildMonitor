namespace BuildMonitor.Core.Models;

public enum AzureConnectionTestOutcome
{
    Success,
    PatMissing,
    AuthenticationRejected,
    OrganizationUnreachable,
    NetworkFailure,
    UnexpectedResponse
}

public sealed record AzureConnectionTestResult(
    AzureConnectionTestOutcome Outcome,
    string Message,
    string? AuthenticatedUserDisplayName = null);

public sealed record AzureProjectSummary(
    string Id,
    string Name,
    string? Description,
    string? State);

public sealed record AzureRepositorySummary(
    string Id,
    string Name,
    string ProjectId,
    string ProjectName,
    string? RemoteUrl,
    string? WebUrl,
    string? DefaultBranch,
    string? DefaultBranchShortName);

public sealed record AzurePipelineSummary(
    int DefinitionId,
    string DisplayName,
    bool IsEnabled,
    string? RepositoryId,
    string? RepositoryName,
    string? RepositoryType,
    string? Path,
    string? WebUrl,
    IReadOnlyList<string> TriggerBranches);
