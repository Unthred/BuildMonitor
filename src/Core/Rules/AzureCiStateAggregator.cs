using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Aggregates per-pipeline representative runs into project CI monitoring state.</summary>
public static class AzureCiStateAggregator
{
    public static AzureCiMonitoringState ToCiState(AzurePipelineRunInfo run)
    {
        if (AzureRunSelector.IsActive(run.State))
        {
            return AzureCiMonitoringState.Activity;
        }

        if (run.State != PipelineRunState.Completed)
        {
            return AzureCiMonitoringState.NotMonitored;
        }

        return run.Result switch
        {
            PipelineRunResult.Failed => AzureCiMonitoringState.Failed,
            PipelineRunResult.PartiallySucceeded => AzureCiMonitoringState.Warning,
            PipelineRunResult.Succeeded => AzureCiMonitoringState.Healthy,
            // Cancelled / unknown completed → Neutral tray contribution (same mapping as NotMonitored in AzureHealthContribution).
            _ => AzureCiMonitoringState.NotMonitored
        };
    }

    public static AzureCiMonitoringState Aggregate(IReadOnlyList<AzurePipelineRunInfo> representatives)
    {
        if (representatives.Count == 0)
        {
            // NoRun → Neutral
            return AzureCiMonitoringState.NotMonitored;
        }

        var mapped = representatives.Select(ToCiState).ToList();
        if (mapped.Any(s => s == AzureCiMonitoringState.Failed))
        {
            return AzureCiMonitoringState.Failed;
        }

        if (mapped.Any(s => s == AzureCiMonitoringState.Warning))
        {
            return AzureCiMonitoringState.Warning;
        }

        if (mapped.Any(s => s == AzureCiMonitoringState.Activity))
        {
            return AzureCiMonitoringState.Activity;
        }

        if (mapped.Any(s => s == AzureCiMonitoringState.Healthy))
        {
            return AzureCiMonitoringState.Healthy;
        }

        return AzureCiMonitoringState.NotMonitored;
    }
}
