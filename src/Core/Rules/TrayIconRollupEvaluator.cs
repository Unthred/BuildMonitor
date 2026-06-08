using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class TrayIconRollupEvaluator
{
    public static PipelineSnapshot? ChooseDisplayedPipeline(IReadOnlyList<PipelineSnapshot> pipelines)
    {
        if (pipelines.Count == 0)
        {
            return null;
        }

        var running = pipelines
            .Where(p => p.State is PipelineRunState.InProgress or PipelineRunState.Canceling)
            .OrderByDescending(p => p.StartedAtUtc ?? p.QueuedAtUtc)
            .FirstOrDefault();

        return running ?? pipelines.OrderByDescending(p => p.FinishedAtUtc ?? p.QueuedAtUtc).First();
    }

    public static MonitorHealth GetHealth(PipelineSnapshot? pipeline)
    {
        if (pipeline is null)
        {
            return MonitorHealth.Unknown;
        }

        if (pipeline.State is PipelineRunState.InProgress or PipelineRunState.Canceling)
        {
            return MonitorHealth.Amber;
        }

        return pipeline.Result switch
        {
            PipelineRunResult.Succeeded => MonitorHealth.Green,
            PipelineRunResult.PartiallySucceeded => MonitorHealth.Amber,
            PipelineRunResult.Unknown => MonitorHealth.Unknown,
            _ => MonitorHealth.Red
        };
    }
}
