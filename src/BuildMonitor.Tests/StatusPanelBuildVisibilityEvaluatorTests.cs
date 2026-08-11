using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelBuildVisibilityEvaluatorTests
{
    [Theory]
    [InlineData(true, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, true)]
    [InlineData(true, ProjectLifecycleState.Building, ProjectLifecycleState.Building, false)]
    [InlineData(false, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, false)]
    [InlineData(true, ProjectLifecycleState.Building, ProjectLifecycleState.BuildOk, false)]
    public void ShouldAutoShow_respects_enabled_and_build_transition(
        bool enabled,
        ProjectLifecycleState previous,
        ProjectLifecycleState current,
        bool expected) =>
        Assert.Equal(
            expected,
            StatusPanelBuildVisibilityEvaluator.ShouldAutoShow(enabled, previous, current));

    [Fact]
    public void ShouldAutoHide_when_auto_shown_and_no_enabled_project_building()
    {
        var projects = new[]
        {
            (ShowWhileBuildingEnabled: true, State: ProjectLifecycleState.Watching),
            (ShowWhileBuildingEnabled: false, State: ProjectLifecycleState.Building)
        };

        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldAutoHide(true, projects));
    }

    [Fact]
    public void ShouldAutoHide_false_while_enabled_project_still_building()
    {
        var projects = new[]
        {
            (ShowWhileBuildingEnabled: true, State: ProjectLifecycleState.Building),
            (ShowWhileBuildingEnabled: true, State: ProjectLifecycleState.Watching)
        };

        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldAutoHide(true, projects));
    }

    [Fact]
    public void ShouldAutoShowForEditGating_on_transition_to_active()
    {
        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldAutoShowForEditGating(
            suppressionEnabled: true,
            isGatingActive: true,
            wasGatingActive: false));
    }

    [Fact]
    public void ShouldAutoHideForEditGating_when_gating_ends()
    {
        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldAutoHideForEditGating(
            autoShownForEditGating: true,
            isGatingActive: false));
    }

    [Theory]
    [InlineData(true, true, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, true)]
    [InlineData(true, false, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, false)]
    [InlineData(true, true, ProjectLifecycleState.Building, ProjectLifecycleState.Building, false)]
    [InlineData(true, true, ProjectLifecycleState.Watching, ProjectLifecycleState.WaitingForEdits, true)]
    [InlineData(true, false, ProjectLifecycleState.Watching, ProjectLifecycleState.WaitingForEdits, true)]
    public void ShouldAutoShowForBusyWork_respects_show_while_building_for_build_states(
        bool suppressionEnabled,
        bool showWhileBuilding,
        ProjectLifecycleState previous,
        ProjectLifecycleState current,
        bool expected) =>
        Assert.Equal(
            expected,
            StatusPanelBuildVisibilityEvaluator.ShouldAutoShowForBusyWork(
                suppressionEnabled,
                showWhileBuilding,
                previous,
                current));

    [Theory]
    [InlineData(false, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, true, true)]
    [InlineData(true, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, true, false)]
    [InlineData(false, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, false, false)]
    public void ShouldContinueThroughBuildFromEditGating(
        bool showWhileBuilding,
        ProjectLifecycleState previous,
        ProjectLifecycleState current,
        bool autoShownForEditGatingOnly,
        bool expected) =>
        Assert.Equal(
            expected,
            StatusPanelBuildVisibilityEvaluator.ShouldContinueThroughBuildFromEditGating(
                showWhileBuilding,
                previous,
                current,
                autoShownForEditGatingOnly));

    [Theory]
    [InlineData(false, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, true, true)]
    [InlineData(true, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, true, false)]
    [InlineData(false, ProjectLifecycleState.Watching, ProjectLifecycleState.Building, false, false)]
    public void ShouldHideWhenBuildStartsWithoutShowSetting(
        bool showWhileBuilding,
        ProjectLifecycleState previous,
        ProjectLifecycleState current,
        bool autoShownForEditGatingOnly,
        bool expected) =>
        Assert.Equal(
            expected,
            StatusPanelBuildVisibilityEvaluator.ShouldHideWhenBuildStartsWithoutShowSetting(
                showWhileBuilding,
                previous,
                current,
                autoShownForEditGatingOnly));

    [Theory]
    [InlineData(ProjectLifecycleState.Watching, ProjectLifecycleState.Building, true)]
    [InlineData(ProjectLifecycleState.Building, ProjectLifecycleState.Building, false)]
    [InlineData(ProjectLifecycleState.Watching, ProjectLifecycleState.WaitingForEdits, true)]
    public void ShouldAutoShowForBusyWork_when_show_while_building_enabled(
        ProjectLifecycleState previous,
        ProjectLifecycleState current,
        bool expected) =>
        Assert.Equal(
            expected,
            StatusPanelBuildVisibilityEvaluator.ShouldAutoShowForBusyWork(
                suppressionEnabled: true,
                showStatusPanelWhileBuilding: true,
                previous,
                current));

    [Fact]
    public void ShouldAutoHideForBusyWork_when_no_project_busy()
    {
        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldAutoHideForBusyWork(
            autoShown: true,
            [ProjectLifecycleState.Watching, ProjectLifecycleState.Running]));
    }

    [Fact]
    public void ShouldKeepPanelVisibleUntilSiteReady_while_listen_url_not_ready()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Running,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5000",
            ListenUrlReady: false,
            SupportsAppRestart: true);

        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldKeepPanelVisibleUntilSiteReady([snapshot]));
    }

    [Fact]
    public void ShouldKeepPanelVisibleUntilSiteReady_false_when_site_is_ready()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Watching,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5000",
            ListenUrlReady: true,
            SupportsAppRestart: true);

        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldKeepPanelVisibleUntilSiteReady([snapshot]));
    }

    [Fact]
    public void ShouldShowSiteReady_false_while_rebuild_restart_in_progress()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Building,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5154",
            ListenUrlReady: true,
            SupportsAppRestart: true,
            IsRestarting: true);

        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldShowSiteStatus(snapshot));
        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldShowSiteReady(snapshot));
        Assert.True(StatusPanelAccentFormatter.ShouldShowAccentRail(snapshot));
    }

    [Fact]
    public void ShouldShowSiteStatus_false_while_building()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Building,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5000",
            ListenUrlReady: false,
            SupportsAppRestart: true);

        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldShowSiteStatus(snapshot));
        Assert.False(StatusPanelBuildVisibilityEvaluator.IsAwaitingSiteReady(snapshot));
    }

    [Fact]
    public void ShouldShowSiteStatus_true_when_running_and_not_ready()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Running,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5000",
            ListenUrlReady: false,
            SupportsAppRestart: true);

        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldShowSiteStatus(snapshot));
        Assert.True(StatusPanelBuildVisibilityEvaluator.IsAwaitingSiteReady(snapshot));
    }

    [Fact]
    public void ShouldShowSiteReady_false_when_rebuild_is_pending()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Watching,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5154",
            ListenUrlReady: true,
            SupportsAppRestart: true,
            IsEditGatingActive: true,
            EditGatingDetailText: "Rebuild queued — post-build cooldown; 3 file(s) arrived.",
            RebuildQuietUntilUtc: DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldShowSiteReady(snapshot));
        Assert.True(StatusPanelBuildVisibilityEvaluator.HasPendingRebuild(snapshot));
    }

    [Fact]
    public void ShouldBlockSiteReadyDismiss_while_awaiting_site()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Running,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5000",
            ListenUrlReady: false,
            SupportsAppRestart: true);

        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldBlockSiteReadyDismiss(snapshot));
    }

    [Fact]
    public void ShouldScheduleSiteReadyDismiss_when_site_up_but_rebuild_queued()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Watching,
            0,
            null,
            null,
            0,
            2000,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5154",
            ListenUrlReady: true,
            SupportsAppRestart: true,
            IsEditGatingActive: true,
            EditGatingDetailText: "Rebuild queued — post-build cooldown; 1 file(s) arrived.",
            RebuildQuietUntilUtc: DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldShowSiteReady(snapshot));
        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldBlockSiteReadyDismiss(snapshot));
        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldScheduleSiteReadyDismiss([snapshot]));
    }

    [Fact]
    public void ShouldShowSiteAwaiting_false_when_probe_ready_even_if_rebuild_pending()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Watching,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            "http://localhost:5154",
            ListenUrlReady: true,
            SupportsAppRestart: true,
            IsEditGatingActive: true,
            RebuildQuietUntilUtc: DateTimeOffset.UtcNow.AddSeconds(2));

        Assert.False(StatusPanelBuildVisibilityEvaluator.ShouldShowSiteAwaiting(snapshot));
    }

    [Fact]
    public void ShouldShowStillEditingButton_when_rebuild_countdown_active()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Waiting",
            ProjectLifecycleState.WaitingForEdits,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            ListenUrlReady: false,
            SupportsAppRestart: true,
            IsEditGatingActive: true,
            RebuildQuietUntilUtc: now.AddSeconds(6));

        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldShowStillEditingButton(snapshot, now));
        Assert.True(StatusPanelBuildVisibilityEvaluator.StillEditingExtendsQuietPeriod(snapshot, now));
    }

    [Fact]
    public void ShouldShowStillEditingButton_when_building_marks_unexpected_mode()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Amber,
            "Building",
            ProjectLifecycleState.Building,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            ListenUrlReady: false,
            SupportsAppRestart: true,
            RebuildQuietUntilUtc: now.AddSeconds(6));

        Assert.True(StatusPanelBuildVisibilityEvaluator.ShouldShowStillEditingButton(snapshot, now));
        Assert.False(StatusPanelBuildVisibilityEvaluator.StillEditingExtendsQuietPeriod(snapshot, now));
    }
}
