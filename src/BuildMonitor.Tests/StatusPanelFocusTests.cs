using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelFocusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    private static ProjectHealthSnapshot Snapshot(
        string id,
        ProjectLifecycleState state = ProjectLifecycleState.Watching,
        bool isActive = true,
        bool isEditGatingActive = false,
        bool isRestarting = false) =>
        new(
            ProjectId: id,
            DisplayName: id,
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: state,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(4),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-1),
            IsActive: isActive,
            ProgressSteps: [],
            IsRestarting: isRestarting,
            IsEditGatingActive: isEditGatingActive,
            LastBuildExitCode: 0);

    private static StatusPanelPresentation Present(params ProjectHealthSnapshot[] snapshots) =>
        StatusPanelPresentationBuilder.Build(snapshots, panelDismissAtUtc: null, Now);

    private static string[] CardIds(StatusPanelPresentation presentation) =>
        presentation.Cards.Select(c => c.ProjectId).ToArray();

    [Theory]
    [InlineData(ProjectLifecycleState.Building, true)]
    [InlineData(ProjectLifecycleState.Testing, true)]
    [InlineData(ProjectLifecycleState.WaitingForEdits, true)]
    [InlineData(ProjectLifecycleState.BuildFailed, true)]
    [InlineData(ProjectLifecycleState.TestFailed, true)]
    [InlineData(ProjectLifecycleState.Crashed, true)]
    [InlineData(ProjectLifecycleState.Watching, false)]
    [InlineData(ProjectLifecycleState.Running, false)]
    [InlineData(ProjectLifecycleState.Idle, false)]
    [InlineData(ProjectLifecycleState.BuildOk, false)]
    public void Busy_states(ProjectLifecycleState state, bool expected) =>
        Assert.Equal(expected, StatusPanelFocus.IsBusy(Snapshot("a", state), includeAzureActivity: true));

    [Fact]
    public void Edit_gating_and_restarting_are_busy()
    {
        Assert.True(StatusPanelFocus.IsBusy(Snapshot("a", isEditGatingActive: true), includeAzureActivity: false));
        Assert.True(StatusPanelFocus.IsBusy(Snapshot("a", isRestarting: true), includeAzureActivity: false));
    }

    [Fact]
    public void Inactive_project_is_never_busy() =>
        Assert.False(StatusPanelFocus.IsBusy(
            Snapshot("a", ProjectLifecycleState.Building, isActive: false),
            includeAzureActivity: true));

    [Fact]
    public void All_projects_scope_keeps_every_card()
    {
        var focus = new StatusPanelFocus();
        var snapshots = new[] { Snapshot("a", ProjectLifecycleState.Building), Snapshot("b"), Snapshot("c") };

        var shown = focus.Apply(Present(snapshots), snapshots, includeAzureActivity: true);

        Assert.Equal(["a", "b", "c"], CardIds(shown));
    }

    [Fact]
    public void Busy_scope_shows_only_busy_projects()
    {
        var focus = new StatusPanelFocus();
        focus.ShowBusyProjects();
        var snapshots = new[] { Snapshot("a"), Snapshot("b", ProjectLifecycleState.Building), Snapshot("c") };

        var shown = focus.Apply(Present(snapshots), snapshots, includeAzureActivity: true);

        Assert.Equal(["b"], CardIds(shown));
        Assert.Equal(3, shown.ActiveProjectCount);
    }

    [Fact]
    public void Project_keeps_its_card_after_its_build_finishes()
    {
        var focus = new StatusPanelFocus();
        focus.ShowBusyProjects();
        var building = new[] { Snapshot("a"), Snapshot("b", ProjectLifecycleState.Building) };
        focus.Apply(Present(building), building, includeAzureActivity: true);

        var finished = new[] { Snapshot("a"), Snapshot("b", ProjectLifecycleState.Watching) };
        var shown = focus.Apply(Present(finished), finished, includeAzureActivity: true);

        Assert.Equal(["b"], CardIds(shown));
    }

    [Fact]
    public void Project_that_starts_building_later_is_added()
    {
        var focus = new StatusPanelFocus();
        focus.ShowBusyProjects();
        var first = new[] { Snapshot("a", ProjectLifecycleState.Building), Snapshot("b"), Snapshot("c") };
        focus.Apply(Present(first), first, includeAzureActivity: true);

        var second = new[] { Snapshot("a"), Snapshot("b"), Snapshot("c", ProjectLifecycleState.Building) };
        var shown = focus.Apply(Present(second), second, includeAzureActivity: true);

        Assert.Equal(["a", "c"], CardIds(shown));
    }

    [Fact]
    public void Busy_scope_with_nothing_busy_yet_shows_every_card()
    {
        var focus = new StatusPanelFocus();
        focus.ShowBusyProjects();
        var snapshots = new[] { Snapshot("a"), Snapshot("b") };

        var shown = focus.Apply(Present(snapshots), snapshots, includeAzureActivity: true);

        Assert.Equal(["a", "b"], CardIds(shown));
    }

    [Fact]
    public void Switching_to_all_projects_and_back_starts_a_fresh_busy_set()
    {
        var focus = new StatusPanelFocus();
        focus.ShowBusyProjects();
        var building = new[] { Snapshot("a", ProjectLifecycleState.Building), Snapshot("b") };
        focus.Apply(Present(building), building, includeAzureActivity: true);

        Assert.True(focus.ShowAllProjects());
        Assert.True(focus.ShowBusyProjects());
        var next = new[] { Snapshot("a"), Snapshot("b", ProjectLifecycleState.Building) };
        var shown = focus.Apply(Present(next), next, includeAzureActivity: true);

        Assert.Equal(["b"], CardIds(shown));
    }

    [Fact]
    public void Repeated_scope_requests_report_no_change()
    {
        var focus = new StatusPanelFocus();

        Assert.False(focus.ShowAllProjects());
        Assert.True(focus.ShowBusyProjects());
        Assert.False(focus.ShowBusyProjects());
    }

    [Theory]
    [InlineData(1, 7, StatusPanelScope.BusyProjects, "Build status (1 of 7)")]
    [InlineData(7, 7, StatusPanelScope.BusyProjects, "Build status (7)")]
    [InlineData(7, 7, StatusPanelScope.AllProjects, "Build status (7)")]
    [InlineData(1, 1, StatusPanelScope.BusyProjects, "Build status")]
    [InlineData(0, 0, StatusPanelScope.AllProjects, "Build status")]
    public void Header_text(int shown, int active, StatusPanelScope scope, string expected) =>
        Assert.Equal(expected, StatusPanelFocus.FormatHeader(shown, active, scope));
}
