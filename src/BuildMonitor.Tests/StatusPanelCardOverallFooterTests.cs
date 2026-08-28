using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

/// <summary>
/// Per-card Overall footer must not use panel-wide SideRail rollup (#102).
/// </summary>
public sealed class StatusPanelCardOverallFooterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Red_plus_green_cards_have_distinct_overall_labels()
    {
        var failed = Snapshot("project-a", "BuildMonitor", MonitorHealth.Red, ProjectLifecycleState.BuildFailed, errorCount: 7);
        var healthy = Snapshot("project-b", "WitherbyConnect", MonitorHealth.Green, ProjectLifecycleState.Watching);

        var presentation = StatusPanelPresentationBuilder.Build([failed, healthy], null, Now);

        Assert.Equal("Needs fix", presentation.Cards[0].OverallLabel);
        Assert.Equal(MonitorHealth.Red, presentation.Cards[0].OverallHealth);
        Assert.Equal("Healthy", presentation.Cards[1].OverallLabel);
        Assert.Equal(MonitorHealth.Green, presentation.Cards[1].OverallHealth);

        Assert.Equal(MonitorHealth.Red, presentation.SideRail.IdleHealth);
        Assert.Equal("Needs fix", presentation.SideRail.IdleLabel);
    }

    [Fact]
    public void Green_plus_red_order_does_not_bleed_needs_fix_to_healthy_card()
    {
        var healthy = Snapshot("project-a", "HealthyFirst", MonitorHealth.Green, ProjectLifecycleState.Watching);
        var failed = Snapshot("project-b", "FailedSecond", MonitorHealth.Red, ProjectLifecycleState.BuildFailed, errorCount: 3);

        var presentation = StatusPanelPresentationBuilder.Build([healthy, failed], null, Now);

        Assert.Equal("Healthy", presentation.Cards[0].OverallLabel);
        Assert.Equal("Needs fix", presentation.Cards[1].OverallLabel);
    }

    [Fact]
    public void Red_and_building_amber_each_get_own_overall_label()
    {
        var failed = Snapshot("project-a", "Failed", MonitorHealth.Red, ProjectLifecycleState.BuildFailed, errorCount: 1);
        var building = Snapshot(
            "project-b",
            "Building",
            MonitorHealth.Amber,
            ProjectLifecycleState.Building) with
        {
            Azure = AzureFacet(
                AzureCiMonitoringState.Activity,
                PipelineRunState.InProgress,
                PipelineRunResult.Unknown)
        };

        var presentation = StatusPanelPresentationBuilder.Build([failed, building], null, Now);

        Assert.Equal("Needs fix", presentation.Cards[0].OverallLabel);
        Assert.Equal("Building", presentation.Cards[1].OverallLabel);
        Assert.Equal(MonitorHealth.Amber, presentation.Cards[1].OverallHealth);
    }

    [Fact]
    public void Two_healthy_projects_both_show_healthy_overall()
    {
        var a = Snapshot("project-a", "A", MonitorHealth.Green, ProjectLifecycleState.Watching);
        var b = Snapshot("project-b", "B", MonitorHealth.Green, ProjectLifecycleState.Watching);

        var presentation = StatusPanelPresentationBuilder.Build([a, b], null, Now);

        Assert.All(presentation.Cards, c => Assert.Equal("Healthy", c.OverallLabel));
        Assert.All(presentation.Cards, c => Assert.Equal(MonitorHealth.Green, c.OverallHealth));
        Assert.Equal("Healthy", presentation.SideRail.IdleLabel);
    }

    [Fact]
    public void Card_order_reversed_preserves_per_project_overall()
    {
        var failed = Snapshot("project-a", "A", MonitorHealth.Red, ProjectLifecycleState.BuildFailed, errorCount: 2);
        var healthy = Snapshot("project-b", "B", MonitorHealth.Green, ProjectLifecycleState.Watching);

        var forward = StatusPanelPresentationBuilder.Build([failed, healthy], null, Now);
        var reverse = StatusPanelPresentationBuilder.Build([healthy, failed], null, Now);

        Assert.Equal(
            forward.Cards.Single(c => c.ProjectId == "project-a").OverallLabel,
            reverse.Cards.Single(c => c.ProjectId == "project-a").OverallLabel);
        Assert.Equal(
            forward.Cards.Single(c => c.ProjectId == "project-b").OverallLabel,
            reverse.Cards.Single(c => c.ProjectId == "project-b").OverallLabel);
    }

    [Fact]
    public void Overall_label_change_triggers_card_rebuild_not_age_only_volatile()
    {
        var building = Snapshot(
            "project-a",
            "Demo",
            MonitorHealth.Amber,
            ProjectLifecycleState.Building) with
        {
            LastBuildFinishedAtUtc = Now.AddMinutes(-1)
        };
        var succeeded = building with
        {
            Health = MonitorHealth.Green,
            State = ProjectLifecycleState.Watching,
            LastBuildFinishedAtUtc = Now
        };

        var previous = StatusPanelPresentationBuilder.Build([building], null, Now);
        var current = StatusPanelPresentationBuilder.Build([succeeded], null, Now.AddMinutes(1));

        Assert.True(StatusPanelPresentationChangeDetector.RequiresCardRebuild(previous, current));
        Assert.False(StatusPanelBuildSourceVolatileRefresher.HasAgeOnlyChanges(previous, current));
    }

    [Fact]
    public void Age_only_volatile_update_preserves_per_card_overall_label()
    {
        var presentation = StatusPanelPresentationBuilder.Build(
            [Snapshot("project-a", "Demo", MonitorHealth.Amber, ProjectLifecycleState.Building)],
            null,
            Now);
        var localRow = Assert.Single(presentation.Cards[0].BuildSourceRows!, r => r.Source == "Local");
        var aged = presentation with
        {
            Cards =
            [
                presentation.Cards[0] with
                {
                    BuildSourceRows = [localRow with { AgeDisplay = "In progress · 2m" }]
                }
            ]
        };

        Assert.False(StatusPanelPresentationChangeDetector.RequiresCardRebuild(presentation, aged));
        Assert.True(StatusPanelBuildSourceVolatileRefresher.HasAgeOnlyChanges(presentation, aged));
        Assert.Equal(presentation.Cards[0].OverallLabel, aged.Cards[0].OverallLabel);
    }

    private static ProjectHealthSnapshot Snapshot(
        string projectId,
        string displayName,
        MonitorHealth health,
        ProjectLifecycleState state,
        int errorCount = 0) =>
        new(
            ProjectId: projectId,
            DisplayName: displayName,
            Health: health,
            HealthLabel: health.ToString(),
            State: state,
            LastExitCode: errorCount > 0 ? 1 : 0,
            LastDuration: TimeSpan.FromSeconds(10),
            LastErrorPreview: errorCount > 0 ? "error" : null,
            ErrorCount: errorCount,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-5),
            IsActive: true,
            ProgressSteps: state == ProjectLifecycleState.Building
                ? [new BuildProgressStep("Restore packages", BuildStepStatus.Complete)]
                : [],
            LastBuildExitCode: errorCount > 0 ? 1 : 0);

    private static ProjectAzureHealthFacet AzureFacet(
        AzureCiMonitoringState ci,
        PipelineRunState runState,
        PipelineRunResult runResult) =>
        new(
            AzureMonitoringAvailability.Available,
            ci,
            "master",
            new AzurePipelineRunInfo(
                1,
                "Pipe",
                507,
                "20260828.19",
                runState,
                runResult,
                "feature/x",
                Now.AddMinutes(-5),
                Now.AddMinutes(-4),
                runState == PipelineRunState.Completed ? Now.AddMinutes(-1) : null,
                "https://example/?buildId=507"),
            [],
            Now,
            HasSelectedPipelines: true);
}
