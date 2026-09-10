using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class StatusPanelActivityAutoShowCycleTests
{
    [Fact]
    public void Deliberate_close_during_Local_hold_suppresses_auto_show()
    {
        var cycle = new StatusPanelActivityAutoShowCycle();
        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [LocalSnapshot(ProjectLifecycleState.Building)],
            keepVisibleDuringLocalBuild: true,
            keepVisibleDuringAzureBuild: true);

        Assert.True(hold);
        Assert.True(cycle.ShouldAutoShowForActivityHold(hold));

        cycle.OnDeliberateDismiss(hold);
        cycle.ObserveActivityHold(hold);

        Assert.True(cycle.IsSuppressed);
        Assert.False(cycle.ShouldAutoShowForActivityHold(hold));
    }

    [Fact]
    public void Deliberate_close_during_Azure_hold_suppresses_auto_show()
    {
        var cycle = new StatusPanelActivityAutoShowCycle();
        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity)],
            keepVisibleDuringLocalBuild: true,
            keepVisibleDuringAzureBuild: true);

        cycle.OnDeliberateDismiss(hold);

        Assert.True(cycle.IsSuppressed);
        Assert.False(cycle.ShouldAutoShowForActivityHold(hold));
    }

    [Fact]
    public void Suppression_persists_through_Building_to_Testing()
    {
        var cycle = SuppressDuring(LocalSnapshot(ProjectLifecycleState.Building));

        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [LocalSnapshot(ProjectLifecycleState.Testing)],
            true,
            true);
        cycle.ObserveActivityHold(hold);

        Assert.True(hold);
        Assert.True(cycle.IsSuppressed);
        Assert.False(cycle.ShouldAutoShowForActivityHold(hold));
    }

    [Fact]
    public void Suppression_persists_through_Testing_to_Restarting()
    {
        var cycle = SuppressDuring(LocalSnapshot(ProjectLifecycleState.Testing));

        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [LocalSnapshot(ProjectLifecycleState.Watching, isRestarting: true)],
            true,
            true);
        cycle.ObserveActivityHold(hold);

        Assert.True(hold);
        Assert.True(cycle.IsSuppressed);
    }

    [Fact]
    public void Local_settles_but_Azure_remains_suppression_stays()
    {
        var cycle = SuppressDuring(LocalSnapshot(ProjectLifecycleState.Building, "p1"));

        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [
                LocalSnapshot(ProjectLifecycleState.Watching, "p1"),
                AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity, "p2")
            ],
            true,
            true);
        cycle.ObserveActivityHold(hold);

        Assert.True(hold);
        Assert.True(cycle.IsSuppressed);
    }

    [Fact]
    public void Multi_project_remaining_activity_keeps_suppression()
    {
        var cycle = SuppressDuring(
            LocalSnapshot(ProjectLifecycleState.Building, "a"),
            AzureSnapshot(PipelineRunState.NotStarted, AzureCiMonitoringState.Activity, "b"));

        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [
                LocalSnapshot(ProjectLifecycleState.Watching, "a"),
                AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity, "b")
            ],
            true,
            true);
        cycle.ObserveActivityHold(hold);

        Assert.True(cycle.IsSuppressed);
    }

    [Fact]
    public void All_activity_settles_clears_suppression()
    {
        var cycle = SuppressDuring(LocalSnapshot(ProjectLifecycleState.Building));

        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [LocalSnapshot(ProjectLifecycleState.Watching)],
            true,
            true);
        cycle.ObserveActivityHold(hold);

        Assert.False(hold);
        Assert.False(cycle.IsSuppressed);
        Assert.False(cycle.ShouldAutoShowForActivityHold(hold));
    }

    [Fact]
    public void Next_activity_cycle_may_auto_show_again()
    {
        var cycle = SuppressDuring(LocalSnapshot(ProjectLifecycleState.Building));
        cycle.ObserveActivityHold(hasQualifyingActivityHold: false);

        var nextHold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [LocalSnapshot(ProjectLifecycleState.Building)],
            true,
            true);
        cycle.ObserveActivityHold(nextHold);

        Assert.False(cycle.IsSuppressed);
        Assert.True(cycle.ShouldAutoShowForActivityHold(nextHold));
    }

    [Fact]
    public void Manual_open_does_not_clear_suppression()
    {
        var cycle = SuppressDuring(LocalSnapshot(ProjectLifecycleState.Building));
        cycle.ObserveActivityHold(hasQualifyingActivityHold: true);

        Assert.True(cycle.IsSuppressed);
        Assert.False(cycle.ShouldAutoShowForActivityHold(true));
    }

    [Fact]
    public void Automatic_hide_without_deliberate_dismiss_does_not_suppress()
    {
        var cycle = new StatusPanelActivityAutoShowCycle();
        cycle.ObserveActivityHold(hasQualifyingActivityHold: true);

        Assert.False(cycle.IsSuppressed);
        Assert.True(cycle.ShouldAutoShowForActivityHold(true));
    }

    [Fact]
    public void Deliberate_dismiss_without_hold_does_not_suppress()
    {
        var cycle = new StatusPanelActivityAutoShowCycle();
        cycle.OnDeliberateDismiss(hasQualifyingActivityHold: false);

        Assert.False(cycle.IsSuppressed);
    }

    [Fact]
    public void Hover_leave_path_does_not_start_suppression()
    {
        // Hover leave only calls Hide/ScheduleHide — never OnDeliberateDismiss.
        var cycle = new StatusPanelActivityAutoShowCycle();
        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [LocalSnapshot(ProjectLifecycleState.Building)],
            true,
            true);
        cycle.ObserveActivityHold(hold);

        Assert.False(cycle.IsSuppressed);
        Assert.True(cycle.ShouldAutoShowForActivityHold(hold));
    }

    [Fact]
    public void Local_off_Azure_on_independence_unchanged_with_cycle()
    {
        var cycle = new StatusPanelActivityAutoShowCycle();
        var localOnly = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [LocalSnapshot(ProjectLifecycleState.Building)],
            keepVisibleDuringLocalBuild: false,
            keepVisibleDuringAzureBuild: true);
        var azureOnly = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(
            [AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity)],
            keepVisibleDuringLocalBuild: false,
            keepVisibleDuringAzureBuild: true);

        Assert.False(localOnly);
        Assert.True(azureOnly);

        cycle.OnDeliberateDismiss(azureOnly);
        Assert.True(cycle.IsSuppressed);
        Assert.False(cycle.ShouldAutoShowForActivityHold(azureOnly));
    }

    [Fact]
    public void Presentation_impact_for_visibility_settings_unchanged()
    {
        var before = new AppSettings();
        var after = Clone(before);
        after.AppBehavior.KeepStatusVisibleDuringLocalBuildActivity = false;

        Assert.Equal(
            SettingsApplyImpact.Presentation,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    private static StatusPanelActivityAutoShowCycle SuppressDuring(params ProjectHealthSnapshot[] snapshots)
    {
        var cycle = new StatusPanelActivityAutoShowCycle();
        var hold = StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, true, true);
        Assert.True(hold);
        cycle.OnDeliberateDismiss(hold);
        cycle.ObserveActivityHold(hold);
        return cycle;
    }

    private static AppSettings Clone(AppSettings source) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(source),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            })!;

    private static ProjectHealthSnapshot LocalSnapshot(
        ProjectLifecycleState state,
        string projectId = "p1",
        bool isActive = true,
        bool isRestarting = false) =>
        new(
            projectId,
            "Demo",
            MonitorHealth.Amber,
            "Building",
            state,
            null,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            isActive,
            [],
            IsRestarting: isRestarting);

    private static ProjectHealthSnapshot AzureSnapshot(
        PipelineRunState runState,
        AzureCiMonitoringState ciState,
        string projectId = "p1") =>
        new(
            projectId,
            "Demo",
            MonitorHealth.Amber,
            "Building",
            ProjectLifecycleState.Watching,
            null,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            Azure: new ProjectAzureHealthFacet(
                AzureMonitoringAvailability.Available,
                ciState,
                FocusBranch: "master",
                PrimaryRun: new AzurePipelineRunInfo(
                    DefinitionId: 1,
                    PipelineDisplayName: "CI",
                    RunId: 42,
                    BuildNumber: "1.0",
                    State: runState,
                    Result: runState == PipelineRunState.Completed
                        ? PipelineRunResult.Succeeded
                        : PipelineRunResult.Unknown,
                    Branch: "master",
                    QueuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                    StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-4),
                    FinishedAtUtc: runState == PipelineRunState.Completed
                        ? DateTimeOffset.UtcNow
                        : null,
                    RunUrl: "https://dev.azure.com/org/proj/_build/results?buildId=42"),
                AttentionRuns: [],
                PolledAtUtc: DateTimeOffset.UtcNow,
                HasSelectedPipelines: true));
}
