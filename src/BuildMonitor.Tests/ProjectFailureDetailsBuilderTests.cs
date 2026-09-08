using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class BuildErrorCompactFormatterTests
{
    [Fact]
    public void Format_compacts_compiler_error_with_file_and_line()
    {
        var raw =
            @"C:\src\Demo\Foo.cs(42,17): error CS1061: 'Bar' does not contain a definition for 'Baz'";

        Assert.Equal(
            "CS1061 · Foo.cs:42 · 'Bar' does not contain a definition for 'Baz'",
            BuildErrorCompactFormatter.Format(raw));
    }

    [Fact]
    public void Format_compacts_msb_without_file()
    {
        Assert.Equal(
            "MSB3021 · Unable to copy file",
            BuildErrorCompactFormatter.Format("error MSB3021: Unable to copy file"));
    }

    [Fact]
    public void Format_keeps_raw_when_unstructured()
    {
        Assert.Equal(
            "The build failed. Fix the error then save the file to try building again.",
            BuildErrorCompactFormatter.Format(
                "The build failed. Fix the error then save the file to try building again."));
    }

    [Fact]
    public void Format_empty_returns_empty()
    {
        Assert.Equal(string.Empty, BuildErrorCompactFormatter.Format("  \n  "));
        Assert.Equal(string.Empty, BuildErrorCompactFormatter.Format(null));
    }
}

public sealed class ProjectFailureDetailsBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_maps_current_local_build_failure()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Watching,
            lastBuildExitCode: 1,
            lastErrorPreview: @"C:\src\A.cs(10,2): error CS1002: ; expected",
            lastFailedBuildNumber: 7,
            health: MonitorHealth.Red);

        var details = ProjectFailureDetailsBuilder.Build(snapshot);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSourceKind.LocalBuild, reason.Source);
        Assert.Equal("Build failed", reason.Title);
        Assert.Equal("CS1002 · A.cs:10 · ; expected", reason.ShortReason);
        Assert.Equal(7, reason.LocalBuildNumber);
        Assert.Contains(reason.Actions, a => a.Kind == FailureActionKind.OpenBuildLog);
        Assert.Contains(reason.Actions, a => a.Kind == FailureActionKind.Rebuild);
        Assert.DoesNotContain(reason.Actions, a => a.Kind == FailureActionKind.RebuildAndRestart);
    }

    [Fact]
    public void Build_maps_current_test_failure()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.TestFailed,
            lastBuildExitCode: 0,
            lastTestFailure: new LocalTestFailureSnapshot(
                FailedCount: 2,
                SkippedCount: 1,
                FailingTestNames: ["FooTests.Bar", "BazTests.Quux"],
                FirstFailureMessage: "Assert.Equal failed",
                OperationId: "op-test-1"),
            health: MonitorHealth.Red);

        var details = ProjectFailureDetailsBuilder.Build(snapshot);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSourceKind.LocalTests, reason.Source);
        Assert.Equal("2 tests failed", reason.Title);
        Assert.Equal("FooTests.Bar · BazTests.Quux", reason.ShortReason);
        Assert.Equal("Assert.Equal failed", reason.Detail);
        Assert.Equal(
            [FailureActionKind.OpenTestLog, FailureActionKind.RunTests],
            reason.Actions.Select(a => a.Kind).ToArray());
    }

    [Fact]
    public void Build_orders_build_before_tests_when_both_current()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.TestFailed,
            lastBuildExitCode: 1,
            lastErrorPreview: "error CS0001: boom",
            lastFailedBuildNumber: 3,
            lastTestFailure: new LocalTestFailureSnapshot(1, 0, ["T.A"], OperationId: "op"),
            supportsRestart: true,
            health: MonitorHealth.Red);

        var details = ProjectFailureDetailsBuilder.Build(snapshot);
        Assert.NotNull(details);
        Assert.Equal(2, details.Reasons.Count);
        Assert.Equal(FailureSourceKind.LocalBuild, details.Primary.Source);
        Assert.Equal(FailureSourceKind.LocalTests, details.Reasons[1].Source);
        Assert.Contains(details.Primary.Actions, a => a.Kind == FailureActionKind.RebuildAndRestart);
    }

    [Fact]
    public void Build_healthy_with_old_failed_history_has_no_failure_card()
    {
        var snapshot = Snapshot(ProjectLifecycleState.Watching, lastBuildExitCode: 0, health: MonitorHealth.Green);
        var history = new[]
        {
            Event(
                OperationalEventKind.Build,
                OperationalEventOutcome.Failed,
                localBuildNumber: 99,
                occurredAtUtc: Now.AddHours(-1),
                detail: new OperationalEventDetail(ErrorPreview: "old build fail"))
        };

        Assert.Null(ProjectFailureDetailsBuilder.Build(snapshot, history));
        Assert.Equal(MonitorHealth.Green, snapshot.Health);
    }

    [Fact]
    public void Build_failure_suppressed_while_Building()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            lastBuildExitCode: 1,
            lastErrorPreview: "error CS0001: stale",
            lastFailedBuildNumber: 8,
            health: MonitorHealth.Amber);

        Assert.Null(ProjectFailureDetailsBuilder.Build(snapshot));
        Assert.False(ProjectFailureDetailsBuilder.IsCurrentLocalBuildFailure(snapshot));
    }

    [Fact]
    public void Build_failure_suppressed_while_Testing()
    {
        // Failed exit code may linger, but Testing means build failure is not authoritative.
        var snapshot = Snapshot(
            ProjectLifecycleState.Testing,
            lastBuildExitCode: 1,
            lastErrorPreview: "error CS0001: stale",
            lastFailedBuildNumber: 8,
            health: MonitorHealth.Amber);

        Assert.Null(ProjectFailureDetailsBuilder.Build(snapshot));
        Assert.False(ProjectFailureDetailsBuilder.IsCurrentLocalBuildFailure(snapshot));
    }

    [Fact]
    public void Successful_rebuild_clears_build_failure_card()
    {
        var failed = Snapshot(
            ProjectLifecycleState.BuildFailed,
            lastBuildExitCode: 1,
            lastErrorPreview: "error CS1002: ; expected",
            lastFailedBuildNumber: 9,
            health: MonitorHealth.Red);
        Assert.NotNull(ProjectFailureDetailsBuilder.Build(failed));

        var recovered = Snapshot(
            ProjectLifecycleState.Watching,
            lastBuildExitCode: 0,
            lastErrorPreview: null,
            lastFailedBuildNumber: null,
            health: MonitorHealth.Green);
        var history = new[]
        {
            Event(
                OperationalEventKind.Build,
                OperationalEventOutcome.Failed,
                localBuildNumber: 9,
                detail: new OperationalEventDetail(ErrorPreview: "error CS1002: ; expected"))
        };

        Assert.Null(ProjectFailureDetailsBuilder.Build(recovered, history));
    }

    [Fact]
    public void Test_failure_cleared_when_rerun_succeeds_or_restarts()
    {
        var failed = Snapshot(
            ProjectLifecycleState.TestFailed,
            lastBuildExitCode: 0,
            lastTestFailure: new LocalTestFailureSnapshot(2, 0, ["A.B"], OperationId: "op-t"),
            health: MonitorHealth.Red);
        Assert.NotNull(ProjectFailureDetailsBuilder.Build(failed));

        // Mid-rerun: state leaves TestFailed (Testing) — card must not stick.
        var rerunning = Snapshot(
            ProjectLifecycleState.Testing,
            lastBuildExitCode: 0,
            lastTestFailure: new LocalTestFailureSnapshot(2, 0, ["A.B"], OperationId: "op-t"),
            health: MonitorHealth.Amber);
        Assert.Null(ProjectFailureDetailsBuilder.Build(rerunning));

        // Success: LastTestFailure cleared by runtime; even if leftover, state gates the card.
        var succeeded = Snapshot(
            ProjectLifecycleState.TestOk,
            lastBuildExitCode: 0,
            lastTestFailure: null,
            health: MonitorHealth.Green);
        var history = new[]
        {
            Event(
                OperationalEventKind.Tests,
                OperationalEventOutcome.Failed,
                operationId: "op-t",
                detail: new OperationalEventDetail(
                    TestFailedCount: 2,
                    FailingTestNames: ["A.B"]))
        };
        Assert.Null(ProjectFailureDetailsBuilder.Build(succeeded, history));
    }

    [Fact]
    public void Build_enriches_build_from_matching_history_id()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.BuildFailed,
            lastBuildExitCode: 1,
            lastErrorPreview: null,
            lastFailedBuildNumber: 12,
            lastFailedTriggerId: "trig-12",
            health: MonitorHealth.Red);

        var history = new[]
        {
            Event(
                OperationalEventKind.Build,
                OperationalEventOutcome.Failed,
                localBuildNumber: 12,
                buildTriggerId: "trig-12",
                detail: new OperationalEventDetail(
                    ExitCode: 1,
                    ErrorPreview: @"src\X.cs(1,1): error CS0103: name does not exist"))
        };

        var details = ProjectFailureDetailsBuilder.Build(snapshot, history);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Contains("CS0103", reason.ShortReason, StringComparison.Ordinal);
        Assert.Equal(12, reason.LocalBuildNumber);
    }

    [Fact]
    public void Build_ignores_unmatched_stale_build_history()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.BuildFailed,
            lastBuildExitCode: 1,
            lastErrorPreview: null,
            lastFailedBuildNumber: 5,
            health: MonitorHealth.Red);

        var history = new[]
        {
            Event(
                OperationalEventKind.Build,
                OperationalEventOutcome.Failed,
                localBuildNumber: 4,
                detail: new OperationalEventDetail(ErrorPreview: "error CS9999: stale"))
        };

        var details = ProjectFailureDetailsBuilder.Build(snapshot, history);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal("Open build log for details", reason.ShortReason);
        Assert.DoesNotContain("CS9999", reason.ShortReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_enriches_tests_only_when_operation_id_matches()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.TestFailed,
            lastBuildExitCode: 0,
            lastTestFailure: new LocalTestFailureSnapshot(1, 0, [], OperationId: "op-match"),
            health: MonitorHealth.Red);

        var history = new[]
        {
            Event(
                OperationalEventKind.Tests,
                OperationalEventOutcome.Failed,
                operationId: "op-match",
                detail: new OperationalEventDetail(
                    TestFailedCount: 3,
                    FailingTestNames: ["A.B", "C.D", "E.F"],
                    ErrorPreview: "A.B — Expected true"))
        };

        var details = ProjectFailureDetailsBuilder.Build(snapshot, history);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal("3 tests failed", reason.Title);
        Assert.Equal("A.B · C.D · E.F", reason.ShortReason);
        Assert.Equal("Expected true", reason.Detail);
    }

    [Fact]
    public void Build_does_not_guess_test_history_without_operation_id()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.TestFailed,
            lastBuildExitCode: 0,
            lastTestFailure: new LocalTestFailureSnapshot(1, 0, ["Local.Only"]),
            health: MonitorHealth.Red);

        var history = new[]
        {
            Event(
                OperationalEventKind.Tests,
                OperationalEventOutcome.Failed,
                operationId: "other-op",
                detail: new OperationalEventDetail(
                    TestFailedCount: 9,
                    FailingTestNames: ["Stale.One"]))
        };

        var details = ProjectFailureDetailsBuilder.Build(snapshot, history);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal("1 test failed", reason.Title);
        Assert.Equal("Local.Only", reason.ShortReason);
    }

    [Fact]
    public void Build_fallback_strings_when_detail_missing()
    {
        var buildDetails = ProjectFailureDetailsBuilder.Build(
            Snapshot(ProjectLifecycleState.BuildFailed, lastBuildExitCode: 1, health: MonitorHealth.Red));
        Assert.NotNull(buildDetails);
        Assert.Equal("Open build log for details", Assert.Single(buildDetails.Reasons).ShortReason);

        var testDetails = ProjectFailureDetailsBuilder.Build(
            Snapshot(
                ProjectLifecycleState.TestFailed,
                lastBuildExitCode: 0,
                lastTestFailure: new LocalTestFailureSnapshot(0, 0, []),
                health: MonitorHealth.Red));
        Assert.NotNull(testDetails);
        var tests = Assert.Single(testDetails.Reasons);
        Assert.Equal("Tests failed", tests.Title);
        Assert.Equal("Open test log for details", tests.ShortReason);
    }

    [Fact]
    public void Presentation_keeps_activity_when_failure_details_exist()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.TestFailed,
            lastBuildExitCode: 0,
            lastTestFailure: new LocalTestFailureSnapshot(1, 0, ["T.A"]),
            health: MonitorHealth.Red);

        var presentation = StatusPanelPresentationBuilder.Build([snapshot], null, Now);
        var card = Assert.Single(presentation.Cards);
        Assert.NotNull(card.FailureDetails);
        Assert.NotNull(card.Activity);
        Assert.Equal(MonitorHealth.Red, card.OverallHealth);
        Assert.False(card.ShowErrorPreview);
    }

    [Fact]
    public void Health_composer_unchanged_by_failure_details_presence()
    {
        var local = Snapshot(
            ProjectLifecycleState.Watching,
            lastBuildExitCode: 0,
            health: MonitorHealth.Green);
        var azure = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            null,
            [],
            Now);
        var composed = ProjectHealthComposer.WithAzure(local, azure);

        Assert.Equal(MonitorHealth.Red, composed.Health);
        // Azure failure alone does not yet produce #111a Local failure details.
        Assert.Null(ProjectFailureDetailsBuilder.Build(composed));
        Assert.Equal(MonitorHealth.Red, composed.Health);
    }

    private static ProjectHealthSnapshot Snapshot(
        ProjectLifecycleState state,
        int lastBuildExitCode = -1,
        string? lastErrorPreview = null,
        int? lastFailedBuildNumber = null,
        string? lastFailedTriggerId = null,
        LocalTestFailureSnapshot? lastTestFailure = null,
        bool supportsRestart = false,
        MonitorHealth health = MonitorHealth.Unknown) =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: health,
            HealthLabel: health.ToString(),
            State: state,
            LastExitCode: lastBuildExitCode >= 0 ? lastBuildExitCode : null,
            LastDuration: TimeSpan.FromSeconds(1),
            LastErrorPreview: lastErrorPreview,
            ErrorCount: state == ProjectLifecycleState.TestFailed
                ? lastTestFailure?.FailedCount ?? 1
                : lastBuildExitCode != 0 ? 1 : 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-1),
            IsActive: true,
            ProgressSteps: [],
            SupportsAppRestart: supportsRestart,
            FailurePhase: HealthIssueCountsFormatter.FormatFailurePhase(state, lastBuildExitCode),
            LastBuildExitCode: lastBuildExitCode,
            LastFailedLocalBuildNumber: lastFailedBuildNumber,
            LastFailedBuildTriggerId: lastFailedTriggerId,
            LastTestFailure: lastTestFailure);

    private static OperationalEvent Event(
        OperationalEventKind kind,
        OperationalEventOutcome outcome,
        int? localBuildNumber = null,
        string? buildTriggerId = null,
        string? operationId = null,
        DateTimeOffset? occurredAtUtc = null,
        OperationalEventDetail? detail = null) =>
        new(
            SchemaVersion: OperationalHistorySchema.CurrentVersion,
            Id: Guid.NewGuid().ToString("N"),
            ProjectId: "p1",
            OccurredAtUtc: occurredAtUtc ?? Now.AddMinutes(-5),
            Source: OperationalEventSource.Local,
            Kind: kind,
            Outcome: outcome,
            Summary: "test event",
            Detail: detail,
            OperationId: operationId,
            BuildTriggerId: buildTriggerId,
            LocalBuildNumber: localBuildNumber);
}
