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
        var summary = StatusPanelOverallFormatter.Summarize(MonitorHealth.Amber, [snapshot]);
        Assert.Equal("Attention · sign-in required", summary.DisplayText);
    }

    [Fact]
    public void Ci_unavailable_overall_attention_ci_unreachable()
    {
        var snapshot = Base() with
        {
            Health = MonitorHealth.Amber,
            Azure = Facet(AzureMonitoringAvailability.Unavailable, AzureCiMonitoringState.NotMonitored)
        };

        var summary = StatusPanelOverallFormatter.Summarize(MonitorHealth.Amber, [snapshot]);

        Assert.Equal("Attention", summary.Label);
        Assert.Equal("CI unreachable", summary.Reason);
    }

    [Theory]
    [InlineData(14, "Attention · 14 warnings")]
    [InlineData(1, "Attention · 1 warning")]
    public void Local_warnings_overall_attention_with_count(int warnings, string expected)
    {
        var snapshot = Base() with { Health = MonitorHealth.Amber, WarningCount = warnings };

        var summary = StatusPanelOverallFormatter.Summarize(MonitorHealth.Amber, [snapshot]);

        Assert.Equal("Attention", summary.Label);
        Assert.Equal(expected, summary.DisplayText);
        Assert.DoesNotContain("Warnings", summary.Label);
    }

    [Fact]
    public void Partial_ci_overall_attention_partially_succeeded()
    {
        var snapshot = Base() with
        {
            Health = MonitorHealth.Amber,
            Azure = Facet(AzureMonitoringAvailability.Available, AzureCiMonitoringState.Warning)
        };

        Assert.Equal(
            "Attention · CI partially succeeded",
            StatusPanelOverallFormatter.Summarize(MonitorHealth.Amber, [snapshot]).DisplayText);
    }

    [Fact]
    public void Rollup_sums_warnings_and_tooltip_lists_each_project()
    {
        var ab588 = Base() with { ProjectId = "1", DisplayName = "AB588", Health = MonitorHealth.Amber, WarningCount = 14 };
        var logCheck = Base() with { ProjectId = "2", DisplayName = "LogCheck", Health = MonitorHealth.Amber, WarningCount = 14 };
        var healthy = Base() with { ProjectId = "3", DisplayName = "Main" };

        var summary = StatusPanelOverallFormatter.Summarize(MonitorHealth.Amber, [ab588, logCheck, healthy]);

        Assert.Equal("Attention · 28 warnings", summary.DisplayText);
        Assert.Equal($"AB588: 14 warnings{Environment.NewLine}LogCheck: 14 warnings", summary.ToolTip);
    }

    [Fact]
    public void Rollup_sign_in_outranks_warnings_but_tooltip_keeps_both()
    {
        var warnings = Base() with { ProjectId = "1", DisplayName = "AB588", Health = MonitorHealth.Amber, WarningCount = 3 };
        var auth = Base() with
        {
            ProjectId = "2",
            DisplayName = "LogCheck",
            Health = MonitorHealth.Amber,
            Azure = Facet(AzureMonitoringAvailability.AuthRequired, AzureCiMonitoringState.NotMonitored)
        };

        var summary = StatusPanelOverallFormatter.Summarize(MonitorHealth.Amber, [warnings, auth]);

        Assert.Equal("sign-in required", summary.Reason);
        Assert.Contains("AB588: 3 warnings", summary.ToolTip);
        Assert.Contains("LogCheck: sign-in required", summary.ToolTip);
    }

    [Fact]
    public void Building_with_old_warnings_stays_building()
    {
        var snapshot = Base() with
        {
            Health = MonitorHealth.Amber,
            State = ProjectLifecycleState.Building,
            WarningCount = 14
        };

        var summary = StatusPanelOverallFormatter.Summarize(MonitorHealth.Amber, [snapshot]);

        Assert.Equal("Building", summary.Label);
        Assert.Null(summary.Reason);
    }

    [Fact]
    public void Held_back_project_card_says_not_started()
    {
        var snapshot = Base() with
        {
            Health = MonitorHealth.Unknown,
            State = ProjectLifecycleState.Idle,
            NotStartedReason = "Not started: limit of 5 active projects reached"
        };

        var summary = StatusPanelOverallFormatter.Summarize(MonitorHealth.Unknown, [snapshot]);

        Assert.Equal("Not started", summary.Label);
        Assert.Equal("Not started: limit of 5 active projects reached", summary.ToolTip);
        Assert.Equal(
            "Monitoring",
            StatusPanelOverallFormatter.Summarize(MonitorHealth.Unknown, [snapshot, Base() with { Health = MonitorHealth.Unknown }]).Label);
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

    private static ProjectAzureHealthFacet Facet(
        AzureMonitoringAvailability availability,
        AzureCiMonitoringState ci) =>
        new(availability, ci, "master", null, [], Now, null, HasSelectedPipelines: true);

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
