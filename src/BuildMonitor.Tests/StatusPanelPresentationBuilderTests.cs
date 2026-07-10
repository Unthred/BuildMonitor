using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelPresentationBuilderTests
{
    [Fact]
    public void Build_hides_site_ready_while_building()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            listenUrl: "http://localhost:5154",
            listenUrlReady: true,
            supportsRestart: true,
            isRestarting: true);

        var presentation = StatusPanelPresentationBuilder.Build([snapshot], panelDismissAtUtc: null, now);

        var card = Assert.Single(presentation.Cards);
        Assert.False(card.ShowSiteReady);
        Assert.False(card.ShowSiteAwaiting);
        Assert.Equal(StatusPanelSideRailMode.Accent, presentation.SideRail.Mode);
    }

    [Fact]
    public void Build_side_rail_and_card_agree_during_site_awaiting()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            ProjectLifecycleState.Watching,
            listenUrl: "http://localhost:5154",
            listenUrlReady: false,
            supportsRestart: true);

        var presentation = StatusPanelPresentationBuilder.Build([snapshot], null, now);

        Assert.False(Assert.Single(presentation.Cards).ShowSiteReady);
        Assert.True(Assert.Single(presentation.Cards).ShowSiteAwaiting);
        Assert.Equal("Starting site", presentation.SideRail.ActivityLabel);
    }

    [Fact]
    public void Build_still_edits_button_when_rebuild_countdown_active()
    {
        var now = DateTimeOffset.UtcNow;
        var waiting = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.WaitingForEdits,
                rebuildQuietUntilUtc: now.AddSeconds(8))],
            null,
            now);

        Assert.True(Assert.Single(waiting.Cards).ShowStillEditingButton);
        Assert.Contains("extend", Assert.Single(waiting.Cards).StillEditingToolTip!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("p1", waiting.HeaderStillEditingProjectId);
    }

    [Fact]
    public void Build_still_edits_button_marks_unexpected_while_building()
    {
        var now = DateTimeOffset.UtcNow;
        var building = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Building)],
            null,
            now);

        var card = Assert.Single(building.Cards);
        Assert.True(card.ShowStillEditingButton);
        Assert.Contains("unexpected", card.StillEditingToolTip!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_still_edits_button_hidden_when_no_countdown_or_build()
    {
        var now = DateTimeOffset.UtcNow;
        var watching = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Watching)],
            null,
            now);

        Assert.False(Assert.Single(watching.Cards).ShowStillEditingButton);
    }

    [Fact]
    public void Build_header_countdown_prefers_closing_over_rebuild()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            ProjectLifecycleState.WaitingForEdits,
            rebuildQuietUntilUtc: now.AddSeconds(12));

        var presentation = StatusPanelPresentationBuilder.Build(
            [snapshot],
            panelDismissAtUtc: now.AddSeconds(4),
            now);

        Assert.Equal("Closing in 4 s", presentation.HeaderCountdownText);
    }

    private static ProjectHealthSnapshot Snapshot(
        ProjectLifecycleState state,
        string? listenUrl = null,
        bool listenUrlReady = false,
        bool supportsRestart = false,
        bool isRestarting = false,
        DateTimeOffset? rebuildQuietUntilUtc = null) =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: state,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(1),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: DateTimeOffset.UtcNow,
            IsActive: true,
            ProgressSteps: [],
            ListenUrl: listenUrl,
            ListenUrlReady: listenUrlReady,
            SupportsAppRestart: supportsRestart,
            IsRestarting: isRestarting,
            RebuildQuietUntilUtc: rebuildQuietUntilUtc);
}
