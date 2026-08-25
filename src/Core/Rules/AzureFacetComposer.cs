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
        IReadOnlyList<AzurePipelineRunInfo> representatives,
        string? focusBranch,
        DateTimeOffset polledAtUtc)
    {
        var (primary, attention) = AzureRunSelector.SelectPrimaryAndAttention(representatives, focusBranch);
        var ci = AzureCiStateAggregator.Aggregate(representatives);
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
