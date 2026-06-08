using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

public static class NotificationTransitionEvaluator
{
    public static IReadOnlyList<MonitoringEvent> Evaluate(
        MonitorSnapshot current,
        MonitorSnapshot? previous,
        Func<PipelineSnapshot, StageSnapshot?, string> deepLinkBuilder,
        IReadOnlyDictionary<int, MonitoredPipelineSettings> pipelineSettings)
    {
        var events = new List<MonitoringEvent>();

        foreach (var pipeline in current.Pipelines)
        {
            var settings = pipelineSettings.TryGetValue(pipeline.PipelineId, out var found)
                ? found
                : new MonitoredPipelineSettings { PipelineId = pipeline.PipelineId };

            var previousPipeline = previous?.Pipelines.FirstOrDefault(p => p.PipelineId == pipeline.PipelineId);
            AddPipelineEventIfNeeded(pipeline, previousPipeline, settings, deepLinkBuilder, events);

            foreach (var stage in pipeline.Stages)
            {
                var previousStage = previousPipeline?.Stages.FirstOrDefault(s => s.StageName.Equals(stage.StageName, StringComparison.OrdinalIgnoreCase));
                AddStageEventIfNeeded(pipeline, stage, previousStage, settings, deepLinkBuilder, events);
            }
        }

        return events;
    }

    private static void AddPipelineEventIfNeeded(
        PipelineSnapshot current,
        PipelineSnapshot? previous,
        MonitoredPipelineSettings settings,
        Func<PipelineSnapshot, StageSnapshot?, string> deepLinkBuilder,
        ICollection<MonitoringEvent> events)
    {
        if (!IsTerminal(current.Result))
        {
            return;
        }

        if (previous is not null && previous.RunId == current.RunId && previous.Result == current.Result)
        {
            return;
        }

        var isRecovery = current.Result == PipelineRunResult.Succeeded &&
                         previous is not null &&
                         previous.Result is PipelineRunResult.Failed or PipelineRunResult.Canceled or PipelineRunResult.PartiallySucceeded;

        if (!ShouldNotify(settings.NotificationMode, current.Result, isRecovery))
        {
            return;
        }

        events.Add(new MonitoringEvent(
            new PipelineTransitionKey(current.PipelineId, current.RunId, null, current.Result),
            $"{current.PipelineName} {current.RunName} {current.Result}",
            $"Branch {current.Branch} result is {current.Result}.",
            MapHealth(current.Result, current.State),
            isRecovery,
            deepLinkBuilder(current, null)));
    }

    private static void AddStageEventIfNeeded(
        PipelineSnapshot pipeline,
        StageSnapshot current,
        StageSnapshot? previous,
        MonitoredPipelineSettings settings,
        Func<PipelineSnapshot, StageSnapshot?, string> deepLinkBuilder,
        ICollection<MonitoringEvent> events)
    {
        if (!IsTerminal(current.Result))
        {
            return;
        }

        if (previous is not null && previous.Result == current.Result)
        {
            return;
        }

        var isRecovery = current.Result == PipelineRunResult.Succeeded &&
                         previous is not null &&
                         previous.Result is PipelineRunResult.Failed or PipelineRunResult.Canceled or PipelineRunResult.PartiallySucceeded;

        if (!ShouldNotify(settings.NotificationMode, current.Result, isRecovery))
        {
            return;
        }

        events.Add(new MonitoringEvent(
            new PipelineTransitionKey(pipeline.PipelineId, pipeline.RunId, current.StageName, current.Result),
            $"{pipeline.PipelineName}/{current.StageName} {current.Result}",
            $"Stage {current.StageName} changed to {current.Result}.",
            MapHealth(current.Result, current.State),
            isRecovery,
            deepLinkBuilder(pipeline, current)));
    }

    private static bool IsTerminal(PipelineRunResult result) =>
        result is PipelineRunResult.Succeeded or PipelineRunResult.PartiallySucceeded or PipelineRunResult.Failed or PipelineRunResult.Canceled;

    private static bool ShouldNotify(NotificationMode mode, PipelineRunResult result, bool isRecovery) =>
        mode switch
        {
            NotificationMode.FailuresOnly => result is PipelineRunResult.Failed or PipelineRunResult.Canceled or PipelineRunResult.PartiallySucceeded,
            NotificationMode.FailuresAndRecovery => isRecovery || result is PipelineRunResult.Failed or PipelineRunResult.Canceled or PipelineRunResult.PartiallySucceeded,
            NotificationMode.AllStateChanges => true,
            _ => false
        };

    private static MonitorHealth MapHealth(PipelineRunResult result, PipelineRunState state)
    {
        if (state is PipelineRunState.InProgress or PipelineRunState.Canceling)
        {
            return MonitorHealth.Amber;
        }

        return result switch
        {
            PipelineRunResult.Succeeded => MonitorHealth.Green,
            PipelineRunResult.PartiallySucceeded => MonitorHealth.Amber,
            PipelineRunResult.Unknown => MonitorHealth.Unknown,
            _ => MonitorHealth.Red
        };
    }
}
