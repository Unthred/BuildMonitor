using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ProjectActivityBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Building_with_restore_step_is_local_restoring()
    {
        var set = ProjectActivityBuilder.Build(
            Snapshot(
                ProjectLifecycleState.Building,
                progressSteps: [new BuildProgressStep("Restore packages", BuildStepStatus.Active)]),
            Now);

        Assert.NotNull(set.Primary);
        Assert.Equal(ActivitySourceKind.Local, set.Primary!.Source);
        Assert.Equal(ActivityPhaseKind.Building, set.Primary.Phase);
        Assert.Equal("Restoring", set.Primary.StatusText);
        Assert.Null(set.Primary.Progress);
    }

    [Fact]
    public void Testing_without_counters_has_no_invented_progress()
    {
        var set = ProjectActivityBuilder.Build(Snapshot(ProjectLifecycleState.Testing), Now);

        Assert.Equal("Running tests", set.PrimaryStatusText);
        Assert.Null(set.Primary!.Progress);
        Assert.Equal("Running tests · 318 / 940", ProjectActivityBuilder.FormatTestingStatus(new ActivityProgress(318, 940)));
        Assert.Equal("Running tests · 318 completed", ProjectActivityBuilder.FormatTestingStatus(null, completedOnly: 318));
    }

    [Fact]
    public void Testing_with_completed_only_does_not_invent_total()
    {
        var started = Now.AddMinutes(-1);
        var set = ProjectActivityBuilder.Build(
            Snapshot(
                ProjectLifecycleState.Testing,
                testProgress: new TestRunLiveProgress(318, started)),
            Now);

        Assert.Equal("Running tests · 318 completed", set.PrimaryStatusText);
        Assert.Null(set.Primary!.Progress);
        Assert.Equal(started, set.Primary.StartedAtUtc);
    }

    [Fact]
    public void Testing_with_authoritative_total_maps_ActivityProgress()
    {
        var started = Now.AddMinutes(-2);
        var set = ProjectActivityBuilder.Build(
            Snapshot(
                ProjectLifecycleState.Testing,
                testProgress: new TestRunLiveProgress(318, started, Total: 940)),
            Now);

        Assert.Equal("Running tests · 318 / 940", set.PrimaryStatusText);
        Assert.Equal(new ActivityProgress(318, 940), set.Primary!.Progress);
        Assert.Equal(started, set.Primary.StartedAtUtc);
    }

    [Fact]
    public void ActivityProgress_TryCreate_rejects_non_positive_total_and_clamps_current()
    {
        Assert.Null(ActivityProgress.TryCreate(5, 0));
        Assert.Null(ActivityProgress.TryCreate(5, -1));
        Assert.Equal(new ActivityProgress(10, 10), ActivityProgress.TryCreate(12, 10));
        Assert.Equal(new ActivityProgress(0, 10), ActivityProgress.TryCreate(-3, 10));
    }

    [Fact]
    public void Local_testing_coexists_with_Azure_activity()
    {
        var set = ProjectActivityBuilder.Build(
            Snapshot(
                ProjectLifecycleState.Testing,
                testProgress: new TestRunLiveProgress(12, Now.AddSeconds(-30)),
                azure: AzureActivityFacet()),
            Now);

        Assert.Equal(2, set.Activities.Count(a => a.IsActive));
        Assert.Equal(ActivitySourceKind.Local, set.Primary!.Source);
        Assert.Equal("Running tests · 12 completed", set.Primary.StatusText);
        Assert.Contains("CI Pipeline", set.CoexistenceSummaryText);
    }

    [Fact]
    public void TestFailed_clears_testing_activity_while_preserving_red_health()
    {
        var snapshot = Snapshot(ProjectLifecycleState.TestFailed, health: MonitorHealth.Red, errorCount: 2);
        var set = ProjectActivityBuilder.Build(snapshot, Now);
        var presentation = StatusPanelPresentationBuilder.Build([snapshot], null, Now);

        Assert.DoesNotContain(set.Activities, a => a.IsActive && a.Phase == ActivityPhaseKind.Testing);
        Assert.Equal(MonitorHealth.Red, presentation.Cards[0].OverallHealth);
    }

    [Fact]
    public void Local_and_Azure_coexist_with_local_primary()
    {
        var set = ProjectActivityBuilder.Build(
            Snapshot(
                ProjectLifecycleState.Building,
                progressSteps: [new BuildProgressStep("App", BuildStepStatus.Active)],
                azure: AzureActivityFacet()),
            Now);

        Assert.Equal(2, set.Activities.Count(a => a.IsActive));
        Assert.Equal(ActivitySourceKind.Local, set.Primary!.Source);
        Assert.Contains("·", set.CoexistenceSummaryText);
        Assert.Contains("in progress", set.CoexistenceSummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Azure_only_activity_uses_pipeline_and_state_not_fake_stage()
    {
        var set = ProjectActivityBuilder.Build(
            Snapshot(ProjectLifecycleState.Watching, azure: AzureActivityFacet()),
            Now);

        Assert.Equal(ActivitySourceKind.Azure, set.Primary!.Source);
        Assert.Equal(ActivityPhaseKind.AzureInProgress, set.Primary.Phase);
        Assert.Equal("CI Pipeline · in progress", set.Primary.StatusText);
        Assert.DoesNotContain("Test stage", set.Primary.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Idle_clears_stale_local_activity()
    {
        var set = ProjectActivityBuilder.Build(Snapshot(ProjectLifecycleState.BuildOk), Now);

        Assert.DoesNotContain(set.Activities, a => a.IsActive);
        Assert.Null(set.Primary);
        Assert.Null(set.PrimaryStatusText);
    }

    [Fact]
    public void Waiting_for_edits_is_represented()
    {
        var set = ProjectActivityBuilder.Build(
            Snapshot(ProjectLifecycleState.WaitingForEdits),
            Now);

        Assert.Equal(ActivityPhaseKind.WaitingForEdits, set.Primary!.Phase);
        Assert.Equal("Waiting for edits", set.Primary.StatusText);
    }

    [Fact]
    public void Failure_health_is_unchanged_by_activity_build()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            health: MonitorHealth.Red,
            errorCount: 3,
            progressSteps: [new BuildProgressStep("App", BuildStepStatus.Active)]);

        var set = ProjectActivityBuilder.Build(snapshot, Now);
        var presentation = StatusPanelPresentationBuilder.Build([snapshot], null, Now);

        Assert.Equal("Compiling App", set.PrimaryStatusText);
        Assert.Equal(MonitorHealth.Red, presentation.Cards[0].OverallHealth);
        Assert.Equal(MonitorHealth.Red, presentation.Cards[0].Health);
    }

    [Fact]
    public void Agent_ship_check_outranks_local_building_label_path()
    {
        var cp = ProjectControlPlaneSnapshot.Unused with
        {
            ShipCheckInProgress = true,
            ShipCheckPhase = ControlPlaneShipCheckPhase.Testing
        };
        var set = ProjectActivityBuilder.Build(
            Snapshot(ProjectLifecycleState.Building, controlPlane: cp),
            Now);

        Assert.Equal(ActivitySourceKind.Agent, set.Primary!.Source);
        Assert.Equal("Ship check — testing", set.Primary.StatusText);
    }

    [Fact]
    public void Accent_formatter_delegates_to_activity_model()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            progressSteps: [new BuildProgressStep("Restore packages", BuildStepStatus.Active)]);

        Assert.Equal(
            ProjectActivityBuilder.FormatRailLabel(snapshot, Now),
            StatusPanelAccentFormatter.FormatActivityLabel(snapshot, Now));
    }

    private static ProjectHealthSnapshot Snapshot(
        ProjectLifecycleState state,
        MonitorHealth health = MonitorHealth.Green,
        int errorCount = 0,
        IReadOnlyList<BuildProgressStep>? progressSteps = null,
        ProjectAzureHealthFacet? azure = null,
        ProjectControlPlaneSnapshot? controlPlane = null,
        TestRunLiveProgress? testProgress = null) =>
        new(
            "p1",
            "Demo",
            health,
            health == MonitorHealth.Red ? "Failed" : "Success",
            state,
            0,
            null,
            null,
            errorCount,
            0,
            Now,
            null,
            true,
            progressSteps ?? [],
            ControlPlane: controlPlane,
            Azure: azure,
            TestProgress: testProgress);

    private static ProjectAzureHealthFacet AzureActivityFacet() =>
        new(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            "refs/heads/main",
            new AzurePipelineRunInfo(
                1,
                "CI Pipeline",
                552,
                "552",
                PipelineRunState.InProgress,
                PipelineRunResult.Unknown,
                "refs/heads/main",
                Now.AddMinutes(-5),
                Now.AddMinutes(-4),
                null,
                "https://example/run/552"),
            [],
            Now);
}
