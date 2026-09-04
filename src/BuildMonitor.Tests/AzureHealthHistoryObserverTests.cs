using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Tests;

public sealed class AzureHealthHistoryObserverTests
{
    [Fact]
    public void New_current_RunId_emits_one_AzureRun_event()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var run = Run(101, PipelineRunState.InProgress, PipelineRunResult.Unknown, "42", "main");

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(run))]);

        var azure = store.Chronological().Where(e => e.Kind == OperationalEventKind.AzureRun).ToList();
        Assert.Single(azure);
        Assert.Equal(OperationalEventSource.Azure, azure[0].Source);
        Assert.Equal(101, azure[0].AzureRunId);
        Assert.Equal("42", azure[0].AzureBuildNumber);
        Assert.Equal("main", azure[0].Branch);
        Assert.Equal(OperationalEventOutcome.Started, azure[0].Outcome);
        Assert.Equal("azure-run-current", azure[0].Detail?.ActionName);
    }

    [Fact]
    public void Same_RunId_repeated_emits_no_duplicate()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var run = Run(101, PipelineRunState.InProgress, PipelineRunResult.Unknown);

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(run))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(run))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(run))]);

        Assert.Equal(1, store.Events.Count(e => e.Kind == OperationalEventKind.AzureRun));
    }

    [Fact]
    public void Same_RunId_status_transition_emits_one_event()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var start = Run(101, PipelineRunState.NotStarted, PipelineRunResult.Unknown, "7");
        var progress = Run(101, PipelineRunState.InProgress, PipelineRunResult.Unknown, "7");

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(start))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(progress))]);

        var azure = store.Chronological().Where(e => e.Kind == OperationalEventKind.AzureRun).ToList();
        Assert.Equal(2, azure.Count);
        Assert.Equal("azure-run-current", azure[0].Detail?.ActionName);
        Assert.Equal("azure-run-transition", azure[1].Detail?.ActionName);
        Assert.Equal("NotStarted/Unknown", azure[1].PreviousValue);
        Assert.Equal("InProgress/Unknown", azure[1].NewValue);
        Assert.Equal(OperationalEventOutcome.Started, azure[1].Outcome);
    }

    [Fact]
    public void Same_RunId_result_transition_emits_one_event()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var building = Run(101, PipelineRunState.InProgress, PipelineRunResult.Unknown, "7");
        var failed = Run(101, PipelineRunState.Completed, PipelineRunResult.Failed, "7");

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(building))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Red, AvailableFacet(failed))]);

        var azure = store.Chronological().Where(e => e.Kind == OperationalEventKind.AzureRun).ToList();
        Assert.Equal(2, azure.Count);
        Assert.Equal("azure-run-transition", azure[1].Detail?.ActionName);
        Assert.Equal(OperationalEventOutcome.Failed, azure[1].Outcome);
        Assert.Equal("InProgress/Unknown", azure[1].PreviousValue);
        Assert.Equal("Completed/Failed", azure[1].NewValue);
    }

    [Fact]
    public void Same_status_and_result_repeated_emits_no_extra_event()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var start = Run(101, PipelineRunState.NotStarted, PipelineRunResult.Unknown);
        var same = Run(101, PipelineRunState.InProgress, PipelineRunResult.Unknown);

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(start))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(same))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(same))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(same))]);

        Assert.Equal(2, store.Events.Count(e => e.Kind == OperationalEventKind.AzureRun));
    }

    [Fact]
    public void Current_run_replaced_by_newer_RunId_emits_one_new_run_event()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var older = Run(101, PipelineRunState.Completed, PipelineRunResult.Succeeded, "10");
        var newer = Run(202, PipelineRunState.InProgress, PipelineRunResult.Unknown, "11", "feature/x");

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(older))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(newer))]);

        var azure = store.Chronological().Where(e => e.Kind == OperationalEventKind.AzureRun).ToList();
        Assert.Equal(2, azure.Count);
        Assert.All(azure, e => Assert.Equal("azure-run-current", e.Detail?.ActionName));
        Assert.Equal(202, azure[1].AzureRunId);
        Assert.Equal("11", azure[1].AzureBuildNumber);
        Assert.Equal("feature/x", azure[1].Branch);
        Assert.Null(azure[1].PreviousValue);
    }

    [Fact]
    public void Transient_AuthRequired_or_Unavailable_does_not_emit_fake_run_transition()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var run = Run(550, PipelineRunState.Completed, PipelineRunResult.Succeeded, "550");

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(run))]);
        observer.ObservePublishedSnapshots([
            Snapshot(MonitorHealth.Green, AzureFacetComposer.AuthRequired(DateTimeOffset.UtcNow, "main", "auth"))]);
        observer.ObservePublishedSnapshots([
            Snapshot(MonitorHealth.Green, AzureFacetComposer.Unavailable(DateTimeOffset.UtcNow, "main", "down"))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(run))]);

        var azure = store.Chronological().Where(e => e.Kind == OperationalEventKind.AzureRun).ToList();
        Assert.Single(azure);
        Assert.Equal(550, azure[0].AzureRunId);
        Assert.Equal("azure-run-current", azure[0].Detail?.ActionName);
        Assert.DoesNotContain(azure, e => e.Detail?.ActionName == "azure-run-transition");
    }

    [Fact]
    public void New_RunId_after_Unavailable_gap_emits_one_azure_run_current()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var older = Run(550, PipelineRunState.Completed, PipelineRunResult.Succeeded, "550");
        var newer = Run(551, PipelineRunState.InProgress, PipelineRunResult.Unknown, "551");

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(older))]);
        observer.ObservePublishedSnapshots([
            Snapshot(MonitorHealth.Green, AzureFacetComposer.Unavailable(DateTimeOffset.UtcNow, "main", "down"))]);
        observer.ObservePublishedSnapshots([
            Snapshot(MonitorHealth.Green, AzureFacetComposer.AuthRequired(DateTimeOffset.UtcNow, "main", "auth"))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(newer))]);

        var azure = store.Chronological().Where(e => e.Kind == OperationalEventKind.AzureRun).ToList();
        Assert.Equal(2, azure.Count);
        Assert.All(azure, e => Assert.Equal("azure-run-current", e.Detail?.ActionName));
        Assert.Equal(550, azure[0].AzureRunId);
        Assert.Equal(551, azure[1].AzureRunId);
        Assert.Equal(OperationalEventOutcome.Started, azure[1].Outcome);
        Assert.DoesNotContain(azure, e => e.Detail?.ActionName == "azure-run-transition");
        Assert.Null(azure[1].PreviousValue);
    }

    [Fact]
    public void Combined_state_and_result_change_emits_single_transition()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var building = Run(101, PipelineRunState.InProgress, PipelineRunResult.Unknown);
        var succeeded = Run(101, PipelineRunState.Completed, PipelineRunResult.Succeeded);

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(building))]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(succeeded))]);

        var transitions = store.Chronological()
            .Where(e => e.Kind == OperationalEventKind.AzureRun && e.Detail?.ActionName == "azure-run-transition")
            .ToList();
        Assert.Single(transitions);
        Assert.Equal(OperationalEventOutcome.Succeeded, transitions[0].Outcome);
        Assert.Equal("InProgress/Unknown", transitions[0].PreviousValue);
        Assert.Equal("Completed/Succeeded", transitions[0].NewValue);
    }

    [Fact]
    public void Health_Green_to_Amber_to_Red_emits_exactly_two_transitions()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green)]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber)]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Red)]);

        var health = store.Chronological().Where(e => e.Kind == OperationalEventKind.HealthTransition).ToList();
        Assert.Equal(2, health.Count);
        Assert.All(health, e =>
        {
            Assert.Equal(OperationalEventSource.System, e.Source);
            Assert.Equal(OperationalEventOutcome.Changed, e.Outcome);
        });
        Assert.Equal("Green", health[0].PreviousValue);
        Assert.Equal("Amber", health[0].NewValue);
        Assert.Equal("Amber", health[1].PreviousValue);
        Assert.Equal("Red", health[1].NewValue);
    }

    [Fact]
    public void Health_Red_recomputed_repeatedly_emits_one_event_total_after_baseline()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green)]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Red)]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Red)]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Red)]);

        Assert.Equal(1, store.Events.Count(e => e.Kind == OperationalEventKind.HealthTransition));
    }

    [Fact]
    public void Health_Red_to_Green_emits_one_recovery_transition()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Red)]);
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green)]);

        var health = store.Chronological().Where(e => e.Kind == OperationalEventKind.HealthTransition).ToList();
        Assert.Single(health);
        Assert.Equal("Red", health[0].PreviousValue);
        Assert.Equal("Green", health[0].NewValue);
    }

    [Fact]
    public void Activity_without_effective_health_change_emits_no_health_transition()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var building = Run(55, PipelineRunState.InProgress, PipelineRunResult.Unknown);
        var stillBuilding = Run(55, PipelineRunState.InProgress, PipelineRunResult.Unknown);

        // Amber stays Amber while Azure activity continues — no health transition after baseline.
        observer.ObservePublishedSnapshots([
            Snapshot(MonitorHealth.Amber, AvailableFacet(building, AzureCiMonitoringState.Activity))]);
        observer.ObservePublishedSnapshots([
            Snapshot(MonitorHealth.Amber, AvailableFacet(stillBuilding, AzureCiMonitoringState.Activity))]);

        Assert.DoesNotContain(store.Events, e => e.Kind == OperationalEventKind.HealthTransition);
        Assert.Equal(1, store.Events.Count(e => e.Kind == OperationalEventKind.AzureRun));
    }

    [Fact]
    public void Azure_failure_with_local_activity_uses_published_effective_health_only()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var failed = Run(9, PipelineRunState.Completed, PipelineRunResult.Failed);

        // Observer does not re-evaluate precedence — only published Health matters.
        observer.ObservePublishedSnapshots([
            Snapshot(MonitorHealth.Amber, AvailableFacet(failed, AzureCiMonitoringState.Failed))]);
        observer.ObservePublishedSnapshots([
            Snapshot(MonitorHealth.Amber, AvailableFacet(failed, AzureCiMonitoringState.Failed))]);

        Assert.DoesNotContain(store.Events, e => e.Kind == OperationalEventKind.HealthTransition);
        Assert.Equal(1, store.Events.Count(e => e.Kind == OperationalEventKind.AzureRun));
    }

    [Fact]
    public void Startup_health_baseline_is_silent_Azure_current_run_emits()
    {
        var store = new FakeOperationalHistoryStore();
        var observer = new AzureHealthHistoryObserver(store);
        var run = Run(1, PipelineRunState.Completed, PipelineRunResult.Succeeded, "1");

        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(run))]);

        Assert.DoesNotContain(store.Events, e => e.Kind == OperationalEventKind.HealthTransition);
        Assert.Equal(1, store.Events.Count(e => e.Kind == OperationalEventKind.AzureRun));
    }

    [Fact]
    public void History_recording_failure_does_not_throw_from_observer()
    {
        var store = new FakeOperationalHistoryStore { ThrowOnRecord = true };
        var observer = new AzureHealthHistoryObserver(store);
        var run = Run(3, PipelineRunState.InProgress, PipelineRunResult.Unknown);

        var ex = Record.Exception(() =>
            observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Green, AvailableFacet(run))]));
        Assert.Null(ex);

        store.ThrowOnRecord = false;
        observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Amber, AvailableFacet(run))]);
        // Prior throw meant no azure baseline stored — next observe may emit current again; must not throw.
        Assert.Null(Record.Exception(() =>
            observer.ObservePublishedSnapshots([Snapshot(MonitorHealth.Red)])));
    }

    [Fact]
    public void Null_store_is_noop()
    {
        var observer = new AzureHealthHistoryObserver(null);
        Assert.Null(Record.Exception(() =>
            observer.ObservePublishedSnapshots([
                Snapshot(MonitorHealth.Green, AvailableFacet(Run(1, PipelineRunState.InProgress, PipelineRunResult.Unknown)))])));
    }

    private static ProjectHealthSnapshot Snapshot(
        MonitorHealth health,
        ProjectAzureHealthFacet? azure = null,
        bool isActive = true) =>
        new(
            "p1",
            "Proj",
            health,
            health.ToString(),
            ProjectLifecycleState.Running,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            isActive,
            [],
            Azure: azure);

    private static ProjectAzureHealthFacet AvailableFacet(
        AzurePipelineRunInfo run,
        AzureCiMonitoringState ci = AzureCiMonitoringState.Healthy) =>
        new(
            AzureMonitoringAvailability.Available,
            ci,
            run.Branch,
            run,
            [],
            DateTimeOffset.UtcNow,
            HasSelectedPipelines: true);

    private static AzurePipelineRunInfo Run(
        long runId,
        PipelineRunState state,
        PipelineRunResult result,
        string? buildNumber = null,
        string? branch = "main") =>
        new(
            DefinitionId: 1,
            PipelineDisplayName: "CI",
            RunId: runId,
            BuildNumber: buildNumber ?? runId.ToString(),
            State: state,
            Result: result,
            Branch: branch ?? "main",
            QueuedAtUtc: DateTimeOffset.UtcNow,
            StartedAtUtc: state == PipelineRunState.InProgress || state == PipelineRunState.Completed
                ? DateTimeOffset.UtcNow
                : null,
            FinishedAtUtc: state == PipelineRunState.Completed ? DateTimeOffset.UtcNow : null,
            RunUrl: null);
}
