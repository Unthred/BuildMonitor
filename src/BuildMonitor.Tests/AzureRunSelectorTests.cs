using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class AzureRunSelectorTests
{
    [Fact]
    public void SelectPipelineRepresentative_prefers_active_over_completed()
    {
        var runs = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master", finished: DateTimeOffset.UtcNow),
            Run(1, "CI", PipelineRunState.InProgress, PipelineRunResult.Unknown, "master", started: DateTimeOffset.UtcNow)
        };

        var selected = AzureRunSelector.SelectPipelineRepresentative(runs, ["master"]);
        Assert.NotNull(selected);
        Assert.Equal(PipelineRunState.InProgress, selected.State);
    }

    [Fact]
    public void SelectPipelineRepresentative_uses_latest_completed_when_no_active()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);
        var runs = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Failed, "master", finished: older, runId: 10),
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Succeeded, "master", finished: newer, runId: 11)
        };

        var selected = AzureRunSelector.SelectPipelineRepresentative(runs, ["master"]);
        Assert.Equal(11, selected!.RunId);
        Assert.Equal(PipelineRunResult.Succeeded, selected.Result);
    }

    [Fact]
    public void SelectPrimary_prefers_focus_branch_when_present()
    {
        var reps = new[]
        {
            Run(1, "CI", PipelineRunState.Completed, PipelineRunResult.Failed, "master", runId: 1),
            Run(1, "CI", PipelineRunState.InProgress, PipelineRunResult.Unknown, "feature/foo", runId: 2)
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
        long runId = 100)
    {
        var queued = DateTimeOffset.UtcNow.AddMinutes(-5);
        return new AzurePipelineRunInfo(
            defId,
            name,
            runId,
            runId.ToString(),
            state,
            result,
            branch,
            queued,
            started,
            finished,
            "https://example/build");
    }
}
