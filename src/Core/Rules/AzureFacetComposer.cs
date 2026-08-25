using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>Composes a <see cref="ProjectAzureHealthFacet"/> from poll results.</summary>
public static class AzureFacetComposer
{
    public static ProjectAzureHealthFacet NotMonitored(DateTimeOffset polledAtUtc, string? focusBranch = null) =>
        new(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.NotMonitored,
            focusBranch,
            null,
            [],
            polledAtUtc,
            HasSelectedPipelines: false);

    public static ProjectAzureHealthFacet AuthRequired(DateTimeOffset polledAtUtc, string? focusBranch, string? message) =>
        new(
            AzureMonitoringAvailability.AuthRequired,
            AzureCiMonitoringState.NotMonitored,
            focusBranch,
            null,
            [],
            polledAtUtc,
            message,
            HasSelectedPipelines: true);

    public static ProjectAzureHealthFacet Unavailable(DateTimeOffset polledAtUtc, string? focusBranch, string? message) =>
        new(
            AzureMonitoringAvailability.Unavailable,
            AzureCiMonitoringState.NotMonitored,
            focusBranch,
            null,
            [],
            polledAtUtc,
            message,
            HasSelectedPipelines: true);

    public static ProjectAzureHealthFacet FromPipelineRuns(
        AzureDevOpsProjectAttachment azure,
        IReadOnlyList<AzurePipelineRunInfo> displayRepresentatives,
        string? focusBranch,
        DateTimeOffset polledAtUtc,
        IReadOnlyList<AzurePipelineRunInfo>? healthRepresentatives = null,
        IReadOnlyList<AzurePipelineRunInfo>? extraAttention = null)
    {
        _ = azure;
        var (primary, attentionFromReps) = AzureRunSelector.SelectPrimaryAndAttention(
            displayRepresentatives,
            focusBranch);

        var attention = new List<AzurePipelineRunInfo>(attentionFromReps);
        if (extraAttention is { Count: > 0 })
        {
            foreach (var run in extraAttention)
            {
                if (primary is not null && run.RunId == primary.RunId)
                {
                    continue;
                }

                if (attention.Any(a => a.RunId == run.RunId))
                {
                    continue;
                }

                attention.Add(run);
            }
        }

        var healthPool = healthRepresentatives ?? displayRepresentatives;
        var ci = AzureCiStateAggregator.Aggregate(healthPool);
        return new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            ci,
            focusBranch,
            primary,
            attention,
            polledAtUtc,
            HasSelectedPipelines: true);
    }
}
