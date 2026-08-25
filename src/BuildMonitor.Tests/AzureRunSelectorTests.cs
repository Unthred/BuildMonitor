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
    public void SelectHealthRepresentative_uses_relevant_completed_not_pr_failure()
    {
        var now = DateTimeOffset.UtcNow;
        var runs = new[]
        {
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "PR #168",
                finished: now, queued: now.AddMinutes(-3), runId: 454, buildNumber: "20260825.15", pr: 168),
            Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
                finished: now.AddMinutes(-40), queued: now.AddMinutes(-50), runId: 452, buildNumber: "20260825.13")
        };

        var health = AzureRunSelector.SelectHealthRepresentative(runs, ["master"]);
        Assert.Equal(452, health!.RunId);
        Assert.Equal(PipelineRunResult.Succeeded, health.Result);
        Assert.Equal(
            AzureCiMonitoringState.Healthy,
            AzureCiStateAggregator.Aggregate([health]));
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
    public void Composer_keeps_display_failed_while_health_stays_healthy_on_master()
    {
        var now = DateTimeOffset.UtcNow;
        var display = Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Failed, "PR #168",
            finished: now, runId: 454, buildNumber: "20260825.15", pr: 168);
        var health = Run(8, "WitherbyConnect", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master",
            finished: now.AddMinutes(-40), runId: 452, buildNumber: "20260825.13");

        var facet = AzureFacetComposer.FromPipelineRuns(
            new AzureDevOpsProjectAttachment
            {
                ConnectionId = "c1",
                AdoProjectId = "p",
                AdoProjectName = "p"
            },
            [display],
            focusBranch: "master",
            now,
            healthRepresentatives: [health]);

        Assert.Equal(454, facet.PrimaryRun!.RunId);
        Assert.Equal(AzureCiMonitoringState.Healthy, facet.CiState);
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
