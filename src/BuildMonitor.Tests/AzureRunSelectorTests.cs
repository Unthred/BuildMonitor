using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class AzureRunSelectorTests
{
    [Fact]
    public void SelectDisplayRepresentative_prefers_active_over_completed()
    {
        var runs = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master", finished: DateTimeOffset.UtcNow, runId: 452),
            Run(1, "CI", PipelineRunState.InProgress, PipelineRunResult.Unknown, "master", started: DateTimeOffset.UtcNow, runId: 454)
        };

        var selected = AzureRunSelector.SelectDisplayRepresentative(runs);
        Assert.Equal(454, selected!.RunId);
        Assert.Equal(PipelineRunState.InProgress, selected.State);
    }

    [Fact]
    public void SelectDisplayRepresentative_active_on_non_health_branch_beats_master_success()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.InProgress, PipelineRunResult.Unknown, "feature/foo",
                started: now, queued: now.AddMinutes(-1), runId: 454, buildNumber: "20260825.15"),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "feature/foo",
                finished: now.AddMinutes(-20), queued: now.AddMinutes(-25), runId: 453, buildNumber: "20260825.14"),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now.AddMinutes(-40), queued: now.AddMinutes(-50), runId: 452, buildNumber: "20260825.13")
        };

        var display = AzureRunSelector.SelectDisplayRepresentative(runs);
        Assert.Equal(454, display!.RunId);
        Assert.Equal(PipelineRunState.InProgress, display.State);

        // Legacy API must not discard active non-relevant branch.
        var legacy = AzureRunSelector.SelectPipelineRepresentative(runs, ["master"]);
        Assert.Equal(454, legacy!.RunId);
    }

    [Fact]
    public void SelectDisplayRepresentative_after_active_completes_shows_that_result_not_older_master()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "PR #168",
                finished: now, queued: now.AddMinutes(-3), runId: 454, buildNumber: "20260825.15", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "PR #168",
                finished: now.AddMinutes(-20), queued: now.AddMinutes(-25), runId: 453, buildNumber: "20260825.14", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now.AddMinutes(-40), queued: now.AddMinutes(-50), runId: 452, buildNumber: "20260825.13")
        };

        var display = AzureRunSelector.SelectDisplayRepresentative(runs);
        Assert.Equal(454, display!.RunId);
        Assert.Equal(PipelineRunResult.Failed, display.Result);
    }

    [Fact]
    public void Live_pr_failure_is_red_even_when_older_master_succeeded()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "PR #168",
                finished: now, queued: now.AddMinutes(-3), runId: 454, buildNumber: "20260825.15", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "PR #168",
                finished: now.AddMinutes(-20), queued: now.AddMinutes(-25), runId: 453, buildNumber: "20260825.14", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now.AddMinutes(-40), queued: now.AddMinutes(-50), runId: 452, buildNumber: "20260825.13")
        };

        var display = AzureRunSelector.SelectDisplayRepresentative(runs);
        var health = AzureRunSelector.SelectHealthRepresentative(runs, ["master"]);
        Assert.Equal(454, display!.RunId);
        Assert.Equal(454, health!.RunId);
        Assert.Equal(AzureCiMonitoringState.Failed, AzureCiStateAggregator.Aggregate([health]));

        var facet = AzureFacetComposer.FromPipelineRuns(
            new AzureDevOpsProjectAttachment { ConnectionId = "c1", AdoProjectId = "p", AdoProjectName = "p" },
            [display],
            "master",
            now,
            healthRepresentatives: [health]);

        Assert.Equal(454, facet.PrimaryRun!.RunId);
        Assert.Equal(AzureCiMonitoringState.Failed, facet.CiState);
        Assert.Equal(MonitorHealth.Red, ProjectHealthComposer.Merge(MonitorHealth.Green, facet));
        Assert.Equal(
            MonitorHealth.Red,
            LocalTrayIconRollupEvaluator.Rollup(
            [
                new ProjectHealthSnapshot(
                    "p1", "Proj", MonitorHealth.Red, "Failed", ProjectLifecycleState.Running,
                    0, null, null, 0, 0, now, null, true, [], Azure: facet)
            ]));
    }

    [Fact]
    public void Active_pr_recovery_is_amber_with_previous_failure_attention()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.InProgress, PipelineRunResult.Unknown, "PR #168",
                started: now, queued: now.AddMinutes(-1), runId: 455, buildNumber: "20260825.16", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "PR #168",
                finished: now.AddMinutes(-10), queued: now.AddMinutes(-15), runId: 454, buildNumber: "20260825.15", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now.AddMinutes(-40), queued: now.AddMinutes(-50), runId: 452, buildNumber: "20260825.13")
        };

        var display = AzureRunSelector.SelectDisplayRepresentative(runs);
        var health = AzureRunSelector.SelectHealthRepresentative(runs, ["master"]);
        var previous = AzureRunSelector.SelectPreviousFailureAttention(runs, display);
        Assert.Equal(455, display!.RunId);
        Assert.Equal(455, health!.RunId);
        Assert.Equal(454, previous!.RunId);
        Assert.Equal(AzureCiMonitoringState.Activity, AzureCiStateAggregator.Aggregate([health]));

        var facet = AzureFacetComposer.FromPipelineRuns(
            new AzureDevOpsProjectAttachment { ConnectionId = "c1", AdoProjectId = "p", AdoProjectName = "p" },
            [display],
            "master",
            now,
            healthRepresentatives: [health],
            extraAttention: [previous]);

        Assert.Equal(MonitorHealth.Amber, ProjectHealthComposer.Merge(MonitorHealth.Green, facet));
        Assert.Contains(facet.AttentionRuns, r => r.RunId == 454);
    }

    [Fact]
    public void Successful_pr_recovery_is_green_despite_older_pr_failure()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "PR #168",
                finished: now, queued: now.AddMinutes(-3), runId: 455, buildNumber: "20260825.16", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "PR #168",
                finished: now.AddMinutes(-10), queued: now.AddMinutes(-15), runId: 454, buildNumber: "20260825.15", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now.AddMinutes(-40), queued: now.AddMinutes(-50), runId: 452, buildNumber: "20260825.13")
        };

        var health = AzureRunSelector.SelectHealthRepresentative(runs, ["master"]);
        Assert.Equal(455, health!.RunId);
        Assert.Equal(AzureCiMonitoringState.Healthy, AzureCiStateAggregator.Aggregate([health]));
        Assert.Equal(
            MonitorHealth.Green,
            ProjectHealthComposer.Merge(
                MonitorHealth.Green,
                AzureFacetComposer.FromPipelineRuns(
                    new AzureDevOpsProjectAttachment { ConnectionId = "c1", AdoProjectId = "p", AdoProjectName = "p" },
                    [health],
                    "master",
                    now,
                    healthRepresentatives: [health])));
    }

    [Fact]
    public void Ancient_feature_failure_does_not_poison_newer_master_success()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now, queued: now.AddMinutes(-5), runId: 500, buildNumber: "20260825.20"),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "feature/ancient",
                finished: now.AddDays(-14), queued: now.AddDays(-14), runId: 100, buildNumber: "20260101.1")
        };

        var health = AzureRunSelector.SelectHealthRepresentative(runs, ["master"]);
        Assert.Equal(500, health!.RunId);
        Assert.Equal(AzureCiMonitoringState.Healthy, AzureCiStateAggregator.Aggregate([health]));
    }

    [Fact]
    public void Multiple_pipelines_worst_current_contribution_wins()
    {
        var now = DateTimeOffset.UtcNow;
        var ci = Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
            finished: now, runId: 10);
        var security = Run(2, "Security", PipelineRunState.Completed, PipelineRunResult.Failed, "master",
            finished: now, runId: 20);
        Assert.Equal(AzureCiMonitoringState.Failed, AzureCiStateAggregator.Aggregate([ci, security]));

        var building = Run(1, "CI", PipelineRunState.InProgress, PipelineRunResult.Unknown, "feature/x",
            started: now, runId: 11);
        var ok = Run(2, "Security", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
            finished: now, runId: 21);
        Assert.Equal(AzureCiMonitoringState.Activity, AzureCiStateAggregator.Aggregate([building, ok]));
    }

    [Fact]
    public void SelectHealthRepresentative_active_any_branch_is_activity()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.InProgress, PipelineRunResult.Unknown, "feature/foo",
                started: now, runId: 454),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now.AddHours(-1), runId: 452)
        };

        var health = AzureRunSelector.SelectHealthRepresentative(runs, ["master"]);
        Assert.Equal(454, health!.RunId);
        Assert.Equal(AzureCiMonitoringState.Activity, AzureCiStateAggregator.ToCiState(health));
    }

    [Fact]
    public void SelectPreviousFailureAttention_when_active()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.InProgress, PipelineRunResult.Unknown, "feature/foo",
                started: now, runId: 454),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "feature/foo",
                finished: now.AddMinutes(-20), runId: 453),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now.AddMinutes(-40), runId: 452)
        };

        var display = AzureRunSelector.SelectDisplayRepresentative(runs);
        var previous = AzureRunSelector.SelectPreviousFailureAttention(runs, display);
        Assert.Equal(453, previous!.RunId);
        Assert.Null(AzureRunSelector.SelectPreviousFailureAttention(runs, runs[2]));
    }

    [Fact]
    public void SelectDisplayRepresentative_uses_latest_completed_when_no_active()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);
        var runs = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Failed, "master", finished: older, runId: 10),
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master", finished: newer, runId: 11)
        };

        var selected = AzureRunSelector.SelectDisplayRepresentative(runs);
        Assert.Equal(11, selected!.RunId);
        Assert.Equal(PipelineRunResult.Succeeded, selected.Result);
    }

    [Fact]
    public void SelectPrimary_prefers_active_over_other_pipeline_failure()
    {
        var reps = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Failed, "master", runId: 1),
            Run(2, "Security", PipelineRunState.InProgress, PipelineRunResult.Unknown, "feature/foo", runId: 2)
        };

        var (primary, attention) = AzureRunSelector.SelectPrimaryAndAttention(reps, "master");
        Assert.Equal(2, primary!.RunId);
        Assert.Contains(attention, r => r.RunId == 1);
    }

    [Fact]
    public void SelectPrimary_prefers_focus_branch_when_present()
    {
        var reps = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Failed, "master", runId: 1),
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Succeeded, "feature/foo", runId: 2)
        };

        var (primary, attention) = AzureRunSelector.SelectPrimaryAndAttention(reps, "feature/foo");
        Assert.Equal(2, primary!.RunId);
        Assert.Contains(attention, r => r.RunId == 1);
    }

    [Fact]
    public void Default_branch_failure_still_attention_when_focus_healthy()
    {
        var reps = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Succeeded, "feature/foo", runId: 2),
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Failed, "master", runId: 1)
        };

        var (primary, attention) = AzureRunSelector.SelectPrimaryAndAttention(reps, "feature/foo");
        Assert.Equal(2, primary!.RunId);
        Assert.Contains(attention, r => r.Result == PipelineRunResult.Failed);
    }

    [Fact]
    public void Aggregate_failed_wins_over_healthy_focus()
    {
        var reps = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Succeeded, "feature/foo"),
            Run(2, "Nightly", PipelineRunState.Completed, PipelineRunResult.Failed, "master")
        };

        Assert.Equal(AzureCiMonitoringState.Failed, AzureCiStateAggregator.Aggregate(reps));
    }

    [Fact]
    public void Aggregate_cancelled_is_neutral()
    {
        var reps = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Canceled, "master")
        };

        Assert.Equal(AzureCiMonitoringState.NotMonitored, AzureCiStateAggregator.Aggregate(reps));
        Assert.Null(AzureHealthContribution.ToTrayContribution(
            AzureCiMonitoringState.NotMonitored,
            AzureMonitoringAvailability.Available));
    }

    [Fact]
    public void Aggregate_partial_is_warning()
    {
        var reps = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.PartiallySucceeded, "master")
        };

        Assert.Equal(AzureCiMonitoringState.Warning, AzureCiStateAggregator.Aggregate(reps));
        Assert.Equal(
            MonitorHealth.Amber,
            AzureHealthContribution.ToTrayContribution(
                AzureCiMonitoringState.Warning,
                AzureMonitoringAvailability.Available));
    }

    [Fact]
    public void Aggregate_empty_is_neutral_norun()
    {
        Assert.Equal(AzureCiMonitoringState.NotMonitored, AzureCiStateAggregator.Aggregate([]));
    }

    private static AzurePipelineRunInfo Run(
        int defId,
        string name,
        PipelineRunState state,
        PipelineRunResult result,
        string branch,
        DateTimeOffset? started = null,
        DateTimeOffset? finished = null,
        DateTimeOffset? queued = null,
        long runId = 100,
        string? buildNumber = null,
        int? pr = null)
    {
        var queuedAt = queued ?? DateTimeOffset.UtcNow.AddMinutes(-5);
        return new AzurePipelineRunInfo(
            defId,
            name,
            runId,
            buildNumber ?? runId.ToString(),
            state,
            result,
            branch,
            queuedAt,
            started,
            finished,
            $"https://dev.azure.com/org/proj/_build/results?buildId={runId}&view=results",
            pr);
    }
}
