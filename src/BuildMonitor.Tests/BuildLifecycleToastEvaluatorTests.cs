using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class BuildLifecycleToastEvaluatorTests
{
    [Fact]
    public void Watch_rebuild_failure_toasts_once_while_lifecycle_stays_watching()
    {
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(2);

        var first = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: true,
            previousState: ProjectLifecycleState.Watching,
            previousBuildFinishedAtUtc: t1,
            currentState: ProjectLifecycleState.Watching,
            currentBuildExitCode: 1,
            currentBuildFinishedAtUtc: t2,
            suppressBuildStartedBecauseFileChange: false);

        var repeat = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: true,
            previousState: ProjectLifecycleState.Watching,
            previousBuildFinishedAtUtc: t2,
            currentState: ProjectLifecycleState.Watching,
            currentBuildExitCode: 1,
            currentBuildFinishedAtUtc: t2,
            suppressBuildStartedBecauseFileChange: false);

        Assert.Equal(BuildLifecycleToastKind.BuildFailed, first);
        Assert.Equal(BuildLifecycleToastKind.None, repeat);
    }

    [Fact]
    public void Second_failed_watch_rebuild_toasts_again_when_finished_at_changes()
    {
        var t2 = DateTimeOffset.UtcNow;
        var t3 = t2.AddSeconds(5);

        var kind = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: true,
            previousState: ProjectLifecycleState.Watching,
            previousBuildFinishedAtUtc: t2,
            currentState: ProjectLifecycleState.Watching,
            currentBuildExitCode: 1,
            currentBuildFinishedAtUtc: t3,
            suppressBuildStartedBecauseFileChange: false);

        Assert.Equal(BuildLifecycleToastKind.BuildFailed, kind);
    }

    [Fact]
    public void Successful_watch_rebuild_does_not_emit_notifier_success_while_staying_watching()
    {
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(1);

        var kind = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: true,
            previousState: ProjectLifecycleState.Watching,
            previousBuildFinishedAtUtc: t1,
            currentState: ProjectLifecycleState.Watching,
            currentBuildExitCode: 0,
            currentBuildFinishedAtUtc: t2,
            suppressBuildStartedBecauseFileChange: false);

        Assert.Equal(BuildLifecycleToastKind.None, kind);
    }

    [Fact]
    public void Direct_build_failed_toasts_once_from_build_result_not_twice()
    {
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(8);

        var kind = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: true,
            previousState: ProjectLifecycleState.Building,
            previousBuildFinishedAtUtc: t1,
            currentState: ProjectLifecycleState.BuildFailed,
            currentBuildExitCode: 1,
            currentBuildFinishedAtUtc: t2,
            suppressBuildStartedBecauseFileChange: false);

        var repeat = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: true,
            previousState: ProjectLifecycleState.BuildFailed,
            previousBuildFinishedAtUtc: t2,
            currentState: ProjectLifecycleState.BuildFailed,
            currentBuildExitCode: 1,
            currentBuildFinishedAtUtc: t2,
            suppressBuildStartedBecauseFileChange: false);

        Assert.Equal(BuildLifecycleToastKind.BuildFailed, kind);
        Assert.Equal(BuildLifecycleToastKind.None, repeat);
    }

    [Fact]
    public void Direct_build_success_still_toasts_from_building_end_state()
    {
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(4);

        var kind = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: true,
            previousState: ProjectLifecycleState.Building,
            previousBuildFinishedAtUtc: t1,
            currentState: ProjectLifecycleState.Watching,
            currentBuildExitCode: 0,
            currentBuildFinishedAtUtc: t2,
            suppressBuildStartedBecauseFileChange: false);

        Assert.Equal(BuildLifecycleToastKind.BuildSucceeded, kind);
    }

    [Fact]
    public void Clean_watching_does_not_toast()
    {
        var t1 = DateTimeOffset.UtcNow;
        var kind = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: true,
            previousState: ProjectLifecycleState.Watching,
            previousBuildFinishedAtUtc: t1,
            currentState: ProjectLifecycleState.Watching,
            currentBuildExitCode: 0,
            currentBuildFinishedAtUtc: t1,
            suppressBuildStartedBecauseFileChange: false);

        Assert.Equal(BuildLifecycleToastKind.None, kind);
    }

    [Fact]
    public void First_observation_does_not_toast_hydrated_failure()
    {
        var kind = BuildLifecycleToastEvaluator.Evaluate(
            hadPrevious: false,
            previousState: ProjectLifecycleState.Idle,
            previousBuildFinishedAtUtc: null,
            currentState: ProjectLifecycleState.Watching,
            currentBuildExitCode: 1,
            currentBuildFinishedAtUtc: DateTimeOffset.UtcNow,
            suppressBuildStartedBecauseFileChange: false);

        Assert.Equal(BuildLifecycleToastKind.None, kind);
    }
}
