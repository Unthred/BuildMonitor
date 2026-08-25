using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Abstractions;

public enum AzureBuildPollOutcome
{
    Ok = 0,
    PatMissing = 1,
    AuthRequired = 2,
    Unavailable = 3
}

public sealed record AzureBuildPollResult(
    AzureBuildPollOutcome Outcome,
    IReadOnlyList<AzurePipelineRunInfo> Runs,
    string? Message = null);

/// <summary>Fetches recent builds for one Azure pipeline definition (v21 attachments).</summary>
public interface IAzureBuildPollClient
{
    Task<AzureBuildPollResult> ListRecentBuildsAsync(
        string organizationUrl,
        string adoProjectIdOrName,
        int definitionId,
        string pipelineDisplayName,
        string? pat,
        CancellationToken cancellationToken);
}
