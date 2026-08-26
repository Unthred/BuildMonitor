using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelOverallFormatterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-26T10:00:00Z");

    [Fact]
    public void Local_green_azure_building_overall_building()
    {
        var snapshot = Snapshot(
            MonitorHealth.Amber,
            ProjectLifecycleState.Watching,
            AzureCiMonitoringState.Activity,
            PipelineRunState.InProgress,
            PipelineRunResult.Unknown);

        Assert.Equal(
            "Building",
            StatusPanelOverallFormatter.FormatLabel(MonitorHealth.Amber, [snapshot]));
    }

    [Fact]
    public void Local_green_azure_green_overall_healthy()
    {
        var snapshot = Snapshot(
            MonitorHealth.Green,
            ProjectLifecycleState.Watching,
            AzureCiMonitoringState.Healthy,
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded);

        Assert.Equal(
            "Healthy",
            StatusPanelOverallFormatter.FormatLabel(MonitorHealth.Green, [snapshot]));
        Assert.Equal("Healthy", StatusPanelOverallFormatter.FormatLabelFromHealth(MonitorHealth.Green));
    }

    [Fact]
    public void Any_failed_overall_needs_fix()
    {
        var snapshot = Snapshot(
            MonitorHealth.Red,
            ProjectLifecycleState.Watching,
            AzureCiMonitoringState.Failed,
            PipelineRunState.Completed,
            PipelineRunResult.Failed);

        Assert.Equal(
            "Needs fix",
            StatusPanelOverallFormatter.FormatLabel(MonitorHealth.Red, [snapshot]));
    }

    [Fact]
    public void Auth_required_overall_attention()
    {
        var snapshot = Base() with
        {
            Health = MonitorHealth.Amber,
            Azure = new ProjectAzureHealthFacet(
                AzureMonitoringAvailability.AuthRequired,
                AzureCiMonitoringState.NotMonitored,
                "master",
                null,
                [],
                Now,
                "Authentication required",
                HasSelectedPipelines: true)
        };

        Assert.Equal(
            "Attention",
            StatusPanelOverallFormatter.FormatLabel(MonitorHealth.Amber, [snapshot]));
    }

    [Fact]
    public void Amber_without_activity_is_attention_not_warnings()
    {
        Assert.Equal("Attention", StatusPanelOverallFormatter.FormatLabelFromHealth(MonitorHealth.Amber));
        Assert.NotEqual("Warnings", StatusPanelOverallFormatter.FormatLabelFromHealth(MonitorHealth.Amber));
    }

    private static ProjectHealthSnapshot Snapshot(
        MonitorHealth health,
        ProjectLifecycleState state,
        AzureCiMonitoringState ci,
        PipelineRunState runState,
        PipelineRunResult runResult) =>
        Base() with
        {
            Health = health,
            State = state,
            Azure = new ProjectAzureHealthFacet(
                AzureMonitoringAvailability.Available,
                ci,
                "master",
                new AzurePipelineRunInfo(
                    1,
                    "Pipe",
                    466,
                    "20260826.10",
                    runState,
                    runResult,
                    "master",
                    Now.AddMinutes(-5),
                    Now.AddMinutes(-5),
                    runState == PipelineRunState.Completed ? Now.AddMinutes(-1) : null,
                    "https://example/?buildId=466"),
                [],
                Now,
                HasSelectedPipelines: true)
        };

    private static ProjectHealthSnapshot Base() =>
        new(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Healthy",
            ProjectLifecycleState.Watching,
            0,
            TimeSpan.FromSeconds(1),
            null,
            0,
            0,
            Now,
            Now.AddMinutes(-10),
            true,
            [],
            LastBuildExitCode: 0);
}
