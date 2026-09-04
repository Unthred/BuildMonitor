using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.Diagnostics;

/// <summary>
/// Observes tray-published health snapshots and emits Azure run / composite-health history (#115).
/// Dedupe is in-memory only and resets on process restart; history is never used as current-state authority.
/// </summary>
internal sealed class AzureHealthHistoryObserver
{
    private readonly IOperationalHistoryStore? store;
    private readonly object sync = new();
    private readonly Dictionary<string, AzureDedupe> azureByProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MonitorHealth> healthBaselineByProject = new(StringComparer.OrdinalIgnoreCase);

    public AzureHealthHistoryObserver(IOperationalHistoryStore? store)
    {
        this.store = store;
    }

    /// <summary>Observe one coalesced publish of project snapshots (best-effort; never throws).</summary>
    public void ObservePublishedSnapshots(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        if (store is null || snapshots.Count == 0)
        {
            return;
        }

        try
        {
            lock (sync)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var snapshot in snapshots)
                {
                    if (string.IsNullOrWhiteSpace(snapshot.ProjectId))
                    {
                        continue;
                    }

                    seen.Add(snapshot.ProjectId);
                    ObserveAzureLocked(snapshot);
                    ObserveHealthLocked(snapshot);
                }

                // Drop dedupe for projects no longer in the published set (inactive / removed).
                foreach (var key in azureByProject.Keys.Where(k => !seen.Contains(k)).ToList())
                {
                    azureByProject.Remove(key);
                }

                foreach (var key in healthBaselineByProject.Keys.Where(k => !seen.Contains(k)).ToList())
                {
                    healthBaselineByProject.Remove(key);
                }
            }
        }
        catch
        {
            // Observability must never affect tray health publish.
        }
    }

    private void ObserveAzureLocked(ProjectHealthSnapshot snapshot)
    {
        var facet = snapshot.Azure;
        if (facet is null)
        {
            azureByProject.Remove(snapshot.ProjectId);
            return;
        }

        // Transient auth/network facets clear PrimaryRun — do not invent run-state transitions.
        if (facet.Availability is AzureMonitoringAvailability.AuthRequired
            or AzureMonitoringAvailability.Unavailable)
        {
            return;
        }

        if (facet.CiState == AzureCiMonitoringState.NotMonitored && facet.PrimaryRun is null)
        {
            azureByProject.Remove(snapshot.ProjectId);
            return;
        }

        var run = facet.PrimaryRun;
        if (run is null)
        {
            return;
        }

        if (!azureByProject.TryGetValue(snapshot.ProjectId, out var prior))
        {
            // Startup: newly discovered current RunId is useful; emit once as baseline current run.
            EmitAzure(snapshot.ProjectId, run, isNewRun: true, priorState: null, priorResult: null);
            azureByProject[snapshot.ProjectId] = new AzureDedupe(run.RunId, run.State, run.Result);
            return;
        }

        if (prior.RunId != run.RunId)
        {
            EmitAzure(snapshot.ProjectId, run, isNewRun: true, prior.State, prior.Result);
            azureByProject[snapshot.ProjectId] = new AzureDedupe(run.RunId, run.State, run.Result);
            return;
        }

        if (prior.State == run.State && prior.Result == run.Result)
        {
            return;
        }

        // Same RunId: one combined transition event for state and/or result change.
        EmitAzure(snapshot.ProjectId, run, isNewRun: false, prior.State, prior.Result);
        azureByProject[snapshot.ProjectId] = new AzureDedupe(run.RunId, run.State, run.Result);
    }

    private void ObserveHealthLocked(ProjectHealthSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            healthBaselineByProject.Remove(snapshot.ProjectId);
            return;
        }

        if (!healthBaselineByProject.TryGetValue(snapshot.ProjectId, out var previous))
        {
            // Startup: establish baseline silently — avoid Unknown→Green spam on every restart.
            healthBaselineByProject[snapshot.ProjectId] = snapshot.Health;
            return;
        }

        if (previous == snapshot.Health)
        {
            return;
        }

        EmitHealth(snapshot.ProjectId, previous, snapshot.Health);
        healthBaselineByProject[snapshot.ProjectId] = snapshot.Health;
    }

    private void EmitAzure(
        string projectId,
        AzurePipelineRunInfo run,
        bool isNewRun,
        PipelineRunState? priorState,
        PipelineRunResult? priorResult)
    {
        var outcome = MapAzureOutcome(run);
        var summary = isNewRun
            ? FormatNewRunSummary(run)
            : FormatTransitionSummary(run, priorState, priorResult);

        var previousValue = isNewRun
            ? null
            : FormatAzureEdge(priorState, priorResult);
        var newValue = FormatAzureEdge(run.State, run.Result);

        OperationalHistoryRecorder.TryRecord(
            store,
            OperationalHistoryRecorder.Create(
                projectId,
                OperationalEventSource.Azure,
                OperationalEventKind.AzureRun,
                outcome,
                summary,
                azureRunId: run.RunId,
                azureBuildNumber: string.IsNullOrWhiteSpace(run.BuildNumber) ? null : run.BuildNumber,
                branch: string.IsNullOrWhiteSpace(run.Branch) ? null : run.Branch,
                previousValue: previousValue,
                newValue: newValue,
                detail: new OperationalEventDetail(
                    ActionName: isNewRun ? "azure-run-current" : "azure-run-transition",
                    AzureStage: $"{run.State}/{run.Result}")));
    }

    private void EmitHealth(string projectId, MonitorHealth previous, MonitorHealth next)
    {
        OperationalHistoryRecorder.TryRecord(
            store,
            OperationalHistoryRecorder.Create(
                projectId,
                OperationalEventSource.System,
                OperationalEventKind.HealthTransition,
                OperationalEventOutcome.Changed,
                $"Health {previous} → {next}",
                previousValue: previous.ToString(),
                newValue: next.ToString(),
                detail: new OperationalEventDetail(ActionName: "composite-health")));
    }

    private static OperationalEventOutcome MapAzureOutcome(AzurePipelineRunInfo run)
    {
        if (AzureRunSelector.IsActive(run.State))
        {
            return OperationalEventOutcome.Started;
        }

        if (run.State == PipelineRunState.Completed)
        {
            return run.Result switch
            {
                PipelineRunResult.Succeeded or PipelineRunResult.PartiallySucceeded =>
                    OperationalEventOutcome.Succeeded,
                PipelineRunResult.Failed => OperationalEventOutcome.Failed,
                PipelineRunResult.Canceled => OperationalEventOutcome.Cancelled,
                _ => OperationalEventOutcome.Changed
            };
        }

        return OperationalEventOutcome.Changed;
    }

    private static string FormatNewRunSummary(AzurePipelineRunInfo run)
    {
        var build = string.IsNullOrWhiteSpace(run.BuildNumber) ? $"run {run.RunId}" : $"#{run.BuildNumber}";
        var branch = string.IsNullOrWhiteSpace(run.Branch) ? "" : $" ({run.Branch})";
        return $"Azure {run.PipelineDisplayName} {build}{branch} — {run.State}/{run.Result}";
    }

    private static string FormatTransitionSummary(
        AzurePipelineRunInfo run,
        PipelineRunState? priorState,
        PipelineRunResult? priorResult)
    {
        var build = string.IsNullOrWhiteSpace(run.BuildNumber) ? $"run {run.RunId}" : $"#{run.BuildNumber}";
        var from = FormatAzureEdge(priorState, priorResult) ?? "?";
        var to = FormatAzureEdge(run.State, run.Result) ?? "?";
        return $"Azure {build} {from} → {to}";
    }

    private static string? FormatAzureEdge(PipelineRunState? state, PipelineRunResult? result)
    {
        if (state is null && result is null)
        {
            return null;
        }

        return $"{state}/{result}";
    }

    private readonly record struct AzureDedupe(long RunId, PipelineRunState State, PipelineRunResult Result);
}
