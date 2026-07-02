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
}
