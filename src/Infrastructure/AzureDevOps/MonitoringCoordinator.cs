using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Optional Azure DevOps polling coordinator (parked module).
/// </summary>
public sealed class MonitoringCoordinator(
    IAzureDevOpsMonitorClient monitorClient,
    IStateStore stateStore)
{
    public async Task<MonitoringCycleResult> ExecuteCycleAsync(
        AzureDevOpsSettings adoSettings,
        Func<PipelineSnapshot, StageSnapshot?, string> deepLinkBuilder,
        CancellationToken cancellationToken)
    {
        var previous = await stateStore.LoadAsync(cancellationToken);
        var current = await monitorClient.GetSnapshotAsync(adoSettings, cancellationToken);

        var pipelineSettings = adoSettings.Pipelines.ToDictionary(p => p.PipelineId);
        var events = NotificationTransitionEvaluator.Evaluate(current, previous, deepLinkBuilder, pipelineSettings);

        await stateStore.SaveAsync(current, cancellationToken);

        var displayed = TrayIconRollupEvaluator.ChooseDisplayedPipeline(current.Pipelines);
        var health = TrayIconRollupEvaluator.GetHealth(displayed);

        return new MonitoringCycleResult(current, displayed, health, events);
    }
}

public sealed record MonitoringCycleResult(
    MonitorSnapshot Snapshot,
    PipelineSnapshot? DisplayedPipeline,
    MonitorHealth Health,
    IReadOnlyList<MonitoringEvent> Events);
