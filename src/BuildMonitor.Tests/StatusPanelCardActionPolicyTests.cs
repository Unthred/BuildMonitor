using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelCardActionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TrayApp_like_rebuild_only_shows_rebuild_not_restart()
    {
        var card = BuildCard("buildmonitor-tray", supportsRestart: false);

        Assert.True(card.ShowRebuildButton);
        Assert.False(card.ShowRestartButtons);
        Assert.True(card.ShowRunTestsButton);
    }

    [Fact]
    public void Restart_capable_project_shows_restart_and_rebuild_and_restart()
    {
        var card = BuildCard("witherby-like", supportsRestart: true);

        Assert.True(card.ShowRebuildButton);
        Assert.True(card.ShowRestartButtons);
        Assert.True(card.ShowRunTestsButton);
    }

    [Fact]
    public void Rebuild_and_restart_requires_both_capabilities()
    {
        var rebuildOnly = Snapshot("p1", "P1", supportsRestart: false);
        var withHost = Snapshot("p2", "P2", supportsRestart: true);

        Assert.True(StatusPanelCardActionRules.ShowRebuild(rebuildOnly));
        Assert.False(StatusPanelCardActionRules.ShowRestart(rebuildOnly));
        Assert.False(StatusPanelCardActionRules.ShowRebuildAndRestart(rebuildOnly));

        Assert.True(StatusPanelCardActionRules.ShowRebuild(withHost));
        Assert.True(StatusPanelCardActionRules.ShowRestart(withHost));
        Assert.True(StatusPanelCardActionRules.ShowRebuildAndRestart(withHost));
    }

    [Fact]
    public void Failed_build_does_not_suppress_rebuild()
    {
        var snapshot = Snapshot("bm", "BuildMonitor.TrayApp", supportsRestart: false) with
        {
            Health = MonitorHealth.Red,
            HealthLabel = "Needs fix",
            State = ProjectLifecycleState.BuildFailed,
            LastBuildExitCode = 1,
            ErrorCount = 1,
            LastErrorPreview = "error CS0001: boom"
        };

        var card = Assert.Single(StatusPanelPresentationBuilder.Build([snapshot], null, Now).Cards);
        Assert.True(card.ShowRebuildButton);
        Assert.False(card.ShowRestartButtons);
        Assert.NotNull(card.FailureDetails);
        Assert.DoesNotContain(
            card.FailureDetails.Primary.Actions,
            a => a.Kind is FailureActionKind.Rebuild or FailureActionKind.RebuildAndRestart);
        Assert.Contains(card.FailureDetails.Primary.Actions, a => a.Kind == FailureActionKind.OpenBuildLog);
    }

    [Fact]
    public void Inactive_project_hides_toolbar_actions()
    {
        var snapshot = Snapshot("idle", "Idle", supportsRestart: true) with { IsActive = false };
        Assert.False(StatusPanelCardActionRules.ShowRebuild(snapshot));
        Assert.False(StatusPanelCardActionRules.ShowRestart(snapshot));
        Assert.False(StatusPanelCardActionRules.ShowRebuildAndRestart(snapshot));
        Assert.False(StatusPanelCardActionRules.ShowRunTests(snapshot));
    }

    [Fact]
    public void Witherby_like_action_set_unchanged_shape()
    {
        var card = BuildCard("witherby", supportsRestart: true);
        Assert.True(card.ShowRebuildButton);
        Assert.True(card.ShowRestartButtons);
        Assert.True(card.ShowRunTestsButton);
    }

    [Fact]
    public void Multiple_cards_keep_distinct_project_ids()
    {
        var presentation = StatusPanelPresentationBuilder.Build(
            [
                Snapshot("project-a", "A", supportsRestart: true),
                Snapshot("project-b", "B", supportsRestart: false)
            ],
            null,
            Now);

        Assert.Equal(["project-a", "project-b"], presentation.Cards.Select(c => c.ProjectId).ToArray());
        Assert.True(presentation.Cards[0].ShowRestartButtons);
        Assert.False(presentation.Cards[1].ShowRestartButtons);
        Assert.All(presentation.Cards, c => Assert.True(c.ShowRebuildButton));
    }

    [Fact]
    public void Local_warnings_row_requests_log_open_not_azure_navigation()
    {
        var snapshot = Snapshot("project-a", "A", supportsRestart: true) with
        {
            Health = MonitorHealth.Amber,
            HealthLabel = "Warnings",
            WarningCount = 2,
            ErrorCount = 0,
            State = ProjectLifecycleState.Watching
        };

        var row = Assert.Single(
            StatusPanelPresentationBuilder.Build([snapshot], null, Now).Cards[0].BuildSourceRows!,
            r => r.Source == "Local");

        Assert.Null(row.AzureNavigation);
        Assert.Equal(LocalBuildStatusLogAction.OpenLogWithWarningsFilter, StatusPanelLocalStatusActionRules.Resolve(row));
    }

    private static StatusPanelCardPresentation BuildCard(string projectId, bool supportsRestart)
    {
        var snapshot = Snapshot(projectId, projectId, supportsRestart);
        return StatusPanelPresentationBuilder.Build([snapshot], null, Now).Cards[0];
    }

    private static ProjectHealthSnapshot Snapshot(string projectId, string displayName, bool supportsRestart) =>
        new(
            ProjectId: projectId,
            DisplayName: displayName,
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: ProjectLifecycleState.Watching,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(10),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-1),
            IsActive: true,
            ProgressSteps: [],
            SupportsAppRestart: supportsRestart,
            ListenUrl: supportsRestart ? "http://localhost:5000" : null);
}
