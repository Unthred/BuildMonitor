using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class AutoOpenLogTransitionEvaluatorTests
{
    [Theory]
    [InlineData(AutoOpenLogMode.Never, MonitorHealth.Green, MonitorHealth.Red, ProjectLifecycleState.Building, ProjectLifecycleState.BuildFailed, false)]
    [InlineData(AutoOpenLogMode.Errors, MonitorHealth.Green, MonitorHealth.Red, ProjectLifecycleState.Watching, ProjectLifecycleState.BuildFailed, false)]
    [InlineData(AutoOpenLogMode.Errors, MonitorHealth.Green, MonitorHealth.Red, ProjectLifecycleState.Building, ProjectLifecycleState.BuildFailed, true)]
    [InlineData(AutoOpenLogMode.Errors, MonitorHealth.Red, MonitorHealth.Red, ProjectLifecycleState.BuildFailed, ProjectLifecycleState.BuildFailed, false)]
    [InlineData(AutoOpenLogMode.Errors, MonitorHealth.Green, MonitorHealth.Red, ProjectLifecycleState.Building, ProjectLifecycleState.Building, false)]
    [InlineData(AutoOpenLogMode.Warnings, MonitorHealth.Green, MonitorHealth.Amber, ProjectLifecycleState.Watching, ProjectLifecycleState.Watching, true)]
    [InlineData(AutoOpenLogMode.Warnings, MonitorHealth.Amber, MonitorHealth.Red, ProjectLifecycleState.Watching, ProjectLifecycleState.BuildFailed, true)]
    [InlineData(AutoOpenLogMode.Always, MonitorHealth.Green, MonitorHealth.Green, ProjectLifecycleState.Building, ProjectLifecycleState.Watching, true)]
    [InlineData(AutoOpenLogMode.Always, MonitorHealth.Green, MonitorHealth.Green, ProjectLifecycleState.Testing, ProjectLifecycleState.TestOk, true)]
    public void ShouldOpen_respects_mode_and_transition(
        AutoOpenLogMode mode,
        MonitorHealth previousHealth,
        MonitorHealth currentHealth,
        ProjectLifecycleState previousState,
        ProjectLifecycleState currentState,
        bool expected) =>
        Assert.Equal(
            expected,
            AutoOpenLogTransitionEvaluator.ShouldOpen(
                mode,
                previousHealth,
                currentHealth,
                previousState,
                currentState,
                errorCount: 0));

    [Fact]
    public void ShouldOpen_errors_mode_on_newly_completed_failed_watch_rebuild()
    {
        var t1 = DateTimeOffset.UtcNow;
        Assert.True(
            AutoOpenLogTransitionEvaluator.ShouldOpen(
                AutoOpenLogMode.Errors,
                MonitorHealth.Green,
                MonitorHealth.Red,
                ProjectLifecycleState.Watching,
                ProjectLifecycleState.Watching,
                errorCount: 2,
                hadPreviousBuildResult: true,
                previousBuildFinishedAtUtc: t1,
                currentBuildExitCode: 1,
                currentBuildFinishedAtUtc: t1.AddSeconds(2)));
    }

    [Fact]
    public void ShouldOpen_errors_mode_false_for_repeat_watch_failure_snapshot()
    {
        var t1 = DateTimeOffset.UtcNow;
        Assert.False(
            AutoOpenLogTransitionEvaluator.ShouldOpen(
                AutoOpenLogMode.Errors,
                MonitorHealth.Red,
                MonitorHealth.Red,
                ProjectLifecycleState.Watching,
                ProjectLifecycleState.Watching,
                errorCount: 2,
                hadPreviousBuildResult: true,
                previousBuildFinishedAtUtc: t1,
                currentBuildExitCode: 1,
                currentBuildFinishedAtUtc: t1));
    }

    [Fact]
    public void ShouldOpen_errors_mode_on_run_crash()
    {
        Assert.True(
            AutoOpenLogTransitionEvaluator.ShouldOpen(
                AutoOpenLogMode.Errors,
                MonitorHealth.Green,
                MonitorHealth.Red,
                ProjectLifecycleState.Running,
                ProjectLifecycleState.Crashed));
    }

    [Fact]
    public void ResolveIssueFilters_selects_errors_for_failed_build_without_parsed_count()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Red,
            "Failed",
            ProjectLifecycleState.BuildFailed,
            LastExitCode: 1,
            LastDuration: TimeSpan.FromSeconds(3),
            LastErrorPreview: "targets(269,5): error : asset missing",
            ErrorCount: 0,
            WarningCount: 1065,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: DateTimeOffset.UtcNow,
            IsActive: true,
            ProgressSteps: []);

        var (errors, warnings) = AutoOpenLogTransitionEvaluator.ResolveIssueFilters(
            AutoOpenLogMode.Errors,
            snapshot);

        Assert.True(errors);
        Assert.False(warnings);
    }

    [Fact]
    public void PreferWarningsFilter_only_when_no_errors()
    {
        Assert.True(AutoOpenLogTransitionEvaluator.PreferWarningsFilter(0, 3));
        Assert.False(AutoOpenLogTransitionEvaluator.PreferWarningsFilter(1, 3));
    }
}
