using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ProjectFailureDetailsRunHostBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Genuine_crash_maps_runhost_failure_with_exit_code()
    {
        var details = ProjectFailureDetailsBuilder.Build(CrashSnapshot(exitCode: 1));
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSourceKind.RunHost, reason.Source);
        Assert.Equal("Run host crashed", reason.Title);
        Assert.Equal("Exit code 1", reason.ShortReason);
        Assert.Equal(1, reason.ExitCode);
        Assert.Equal(BuildLogKind.Run, reason.LogKind);
        Assert.Equal([FailureActionKind.OpenRunLog], reason.Actions.Select(a => a.Kind).ToArray());
        Assert.DoesNotContain(reason.Actions, a => a.Kind == FailureActionKind.RebuildAndRestart);
        Assert.DoesNotContain(reason.Actions, a => a.Kind == FailureActionKind.Rebuild);
    }

    [Fact]
    public void Preview_shown_when_crash_correlated()
    {
        var details = ProjectFailureDetailsBuilder.Build(
            CrashSnapshot(exitCode: 1, lastErrorPreview: "Unhandled exception: boom", lastBuildExitCode: 0));
        Assert.NotNull(details);
        Assert.Equal("Exit code 1 · Unhandled exception: boom", Assert.Single(details.Reasons).ShortReason);
    }

    [Fact]
    public void Preview_suppressed_when_likely_stale_build_error()
    {
        var details = ProjectFailureDetailsBuilder.Build(
            CrashSnapshot(
                exitCode: 1,
                lastErrorPreview: "error CS1002: ; expected",
                lastBuildExitCode: 1));
        Assert.NotNull(details);
        var runHost = Assert.Single(details.Reasons, r => r.Source == FailureSourceKind.RunHost);
        Assert.Equal("Exit code 1", runHost.ShortReason);
        Assert.DoesNotContain("CS1002", runHost.ShortReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Intentional_restart_suppresses_runhost_failure()
    {
        Assert.Null(ProjectFailureDetailsBuilder.Build(
            CrashSnapshot(exitCode: 1, isRestarting: true)));
        Assert.False(ProjectFailureDetailsBuilder.IsCurrentRunHostFailure(
            CrashSnapshot(exitCode: 1, isRestarting: true)));
    }

    [Fact]
    public void Desired_stopped_clears_current_runhost_failure()
    {
        var snapshot = CrashSnapshot(exitCode: 1) with
        {
            DesiredRunHostState = DesiredRunHostState.Stopped
        };
        Assert.Null(ProjectFailureDetailsBuilder.Build(snapshot));
        Assert.False(ProjectFailureDetailsBuilder.IsCurrentRunHostFailure(snapshot));
    }

    [Fact]
    public void Desired_running_keeps_current_reason()
    {
        Assert.NotNull(ProjectFailureDetailsBuilder.Build(CrashSnapshot(exitCode: 42)));
    }

    [Fact]
    public void Idle_after_stop_has_no_runhost_failure()
    {
        var stopped = CrashSnapshot(exitCode: 1) with
        {
            State = ProjectLifecycleState.Idle,
            DesiredRunHostState = DesiredRunHostState.Stopped,
            Health = MonitorHealth.Green
        };
        Assert.Null(ProjectFailureDetailsBuilder.Build(stopped));
    }

    [Fact]
    public void Crash_recovery_to_running_clears_card()
    {
        Assert.NotNull(ProjectFailureDetailsBuilder.Build(CrashSnapshot(exitCode: 1)));

        var recovered = CrashSnapshot(exitCode: 1) with
        {
            State = ProjectLifecycleState.Running,
            LastExitCode = 0,
            LastErrorPreview = null,
            Health = MonitorHealth.Green
        };
        Assert.Null(ProjectFailureDetailsBuilder.Build(recovered));
    }

    [Fact]
    public void Rebuild_restart_building_clears_stale_crash()
    {
        var rebuilding = CrashSnapshot(exitCode: 1) with
        {
            State = ProjectLifecycleState.Building,
            IsRestarting = true,
            Health = MonitorHealth.Amber
        };
        Assert.Null(ProjectFailureDetailsBuilder.Build(rebuilding));
    }

    [Fact]
    public void Stale_historical_crash_ignored_after_healthy_recovery()
    {
        var healthy = CrashSnapshot(exitCode: 0) with
        {
            State = ProjectLifecycleState.Watching,
            DesiredRunHostState = DesiredRunHostState.Running,
            LastErrorPreview = null,
            Health = MonitorHealth.Green
        };
        var history = new[]
        {
            RunHostCrashEvent(exitCode: 1, preview: "old crash", occurredAtUtc: Now.AddHours(-1))
        };
        Assert.Null(ProjectFailureDetailsBuilder.Build(healthy, history));
    }

    [Fact]
    public void Current_crash_enriches_from_matching_history()
    {
        var history = new[]
        {
            RunHostCrashEvent(exitCode: 7, preview: "host blew up", occurredAtUtc: Now.AddSeconds(-1))
        };
        var details = ProjectFailureDetailsBuilder.Build(
            CrashSnapshot(exitCode: null, lastErrorPreview: null, lastBuildExitCode: 0),
            history);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(7, reason.ExitCode);
        Assert.Equal("Exit code 7 · host blew up", reason.ShortReason);
    }

    [Fact]
    public void Previous_crash_history_does_not_enrich_new_current_crash()
    {
        // Crash A recorded earlier; current crash B has authoritative exit/preview and no B history yet.
        var history = new[]
        {
            RunHostCrashEvent(
                exitCode: 9,
                preview: "crash A stale preview",
                occurredAtUtc: Now.AddMinutes(-10))
        };
        var current = CrashSnapshot(
            exitCode: 3,
            lastErrorPreview: "crash B live",
            lastBuildExitCode: 0) with
        {
            LastChangedUtc = Now
        };

        var details = ProjectFailureDetailsBuilder.Build(current, history);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(3, reason.ExitCode);
        Assert.Equal("Exit code 3 · crash B live", reason.ShortReason);
        Assert.DoesNotContain("crash A", reason.ShortReason, StringComparison.Ordinal);
        Assert.DoesNotContain("9", reason.ShortReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_build_orders_before_runhost()
    {
        var snapshot = CrashSnapshot(exitCode: 1) with
        {
            LastBuildExitCode = 1,
            LastErrorPreview = "error CS0001: boom",
            LastFailedLocalBuildNumber = 3,
            State = ProjectLifecycleState.Crashed,
            Health = MonitorHealth.Red
        };
        // Crashed suppresses Local build failure (Building/Testing only) — build exit still current.
        // Local build failure allows Crashed state (not in suppression list).
        var details = ProjectFailureDetailsBuilder.Build(snapshot);
        Assert.NotNull(details);
        Assert.Equal(2, details.Reasons.Count);
        Assert.Equal(FailureSourceKind.LocalBuild, details.Primary.Source);
        Assert.Equal(FailureSourceKind.RunHost, details.Reasons[1].Source);
    }

    [Fact]
    public void Runhost_orders_before_azure()
    {
        var run = new AzurePipelineRunInfo(
            1,
            "CI",
            55,
            "55",
            PipelineRunState.Completed,
            PipelineRunResult.Failed,
            "master",
            Now.AddMinutes(-10),
            Now.AddMinutes(-9),
            Now.AddMinutes(-1),
            "https://dev.azure.com/org/p/_build/results?buildId=55&view=results");
        var azure = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            run,
            [],
            Now);
        var snapshot = CrashSnapshot(exitCode: 1) with { Azure = azure, Health = MonitorHealth.Red };

        var details = ProjectFailureDetailsBuilder.Build(snapshot);
        Assert.NotNull(details);
        Assert.Equal(2, details.Reasons.Count);
        Assert.Equal(FailureSourceKind.RunHost, details.Primary.Source);
        Assert.Equal(FailureSourceKind.AzureCi, details.Reasons[1].Source);
        Assert.StartsWith("Azure · #55 failed", details.Reasons[1].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void RunMode_none_never_gets_runhost_failure()
    {
        var snapshot = CrashSnapshot(exitCode: 1, supportsRestart: false);
        Assert.False(snapshot.SupportsAppRestart);
        Assert.Null(ProjectFailureDetailsBuilder.Build(snapshot));
    }

    [Fact]
    public void Health_composer_unchanged_by_runhost_failure_details()
    {
        var snapshot = CrashSnapshot(exitCode: 1);
        Assert.Equal(MonitorHealth.Red, snapshot.Health);
        Assert.NotNull(ProjectFailureDetailsBuilder.Build(snapshot));
        Assert.Equal(MonitorHealth.Red, snapshot.Health);
        Assert.Equal(
            MonitorHealth.Red,
            ProjectHealthEvaluator.Evaluate(snapshot.State, snapshot.LastBuildExitCode, 1, 0));
    }

    [Fact]
    public void Presentation_keeps_activity_with_runhost_failure()
    {
        var snapshot = CrashSnapshot(exitCode: 1);
        var presentation = StatusPanelPresentationBuilder.Build(
            [snapshot],
            null,
            Now);
        var card = Assert.Single(presentation.Cards);
        Assert.NotNull(card.FailureDetails);
        Assert.Equal(FailureSourceKind.RunHost, card.FailureDetails.Primary.Source);
        Assert.NotNull(card.Activity);
        Assert.False(card.ShowErrorPreview);
        Assert.True(StatusPanelCardActionRules.ShowRestart(snapshot));
        Assert.True(StatusPanelCardActionRules.ShowRebuildAndRestart(snapshot));
    }

    [Fact]
    public void Intentional_stop_policy_still_blocks_crash_recovery()
    {
        Assert.False(RunHostLifecyclePolicy.MayApplyCrashRecovery(
            DesiredRunHostState.Stopped,
            exitCode: 1,
            restartOnCrash: true,
            restartCount: 0,
            maxRestartRetries: 3));
        Assert.Null(ProjectFailureDetailsBuilder.Build(
            CrashSnapshot(exitCode: 1) with { DesiredRunHostState = DesiredRunHostState.Stopped }));
    }

    private static ProjectHealthSnapshot CrashSnapshot(
        int? exitCode,
        string? lastErrorPreview = null,
        int lastBuildExitCode = 0,
        bool supportsRestart = true,
        bool isRestarting = false) =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: MonitorHealth.Red,
            HealthLabel: "Failed",
            State: ProjectLifecycleState.Crashed,
            LastExitCode: exitCode,
            LastDuration: TimeSpan.FromSeconds(1),
            LastErrorPreview: lastErrorPreview,
            ErrorCount: 1,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-5),
            IsActive: true,
            ProgressSteps: [],
            SupportsAppRestart: supportsRestart,
            IsRestarting: isRestarting,
            FailurePhase: "Run failed",
            LastBuildExitCode: lastBuildExitCode,
            DesiredRunHostState: DesiredRunHostState.Running);

    private static OperationalEvent RunHostCrashEvent(
        int exitCode,
        string? preview,
        DateTimeOffset occurredAtUtc) =>
        new(
            SchemaVersion: OperationalHistorySchema.CurrentVersion,
            Id: Guid.NewGuid().ToString("N"),
            ProjectId: "p1",
            OccurredAtUtc: occurredAtUtc,
            Source: OperationalEventSource.Local,
            Kind: OperationalEventKind.RunHost,
            Outcome: OperationalEventOutcome.Failed,
            Summary: "Host crashed",
            Detail: new OperationalEventDetail(
                ExitCode: exitCode,
                ErrorPreview: preview,
                LogKind: BuildLogKind.Run,
                ActionName: "host-crashed"));
}
