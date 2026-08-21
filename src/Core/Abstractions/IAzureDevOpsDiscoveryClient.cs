using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Abstractions;

/// <summary>Authenticated Azure DevOps connection test and discovery (no continuous polling).</summary>
public interface IAzureDevOpsDiscoveryClient
{
    Task<AzureConnectionTestResult> TestConnectionAsync(
        AzureDevOpsConnectionSettings connection,
        string? pat,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AzureProjectSummary>> ListProjectsAsync(
        AzureDevOpsConnectionSettings connection,
        string pat,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AzureRepositorySummary>> ListRepositoriesAsync(
        AzureDevOpsConnectionSettings connection,
        string pat,
        string projectIdOrName,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AzurePipelineSummary>> ListPipelinesForRepositoryAsync(
        AzureDevOpsConnectionSettings connection,
        string pat,
        string projectIdOrName,
        string repositoryId,
        CancellationToken cancellationToken);
}
