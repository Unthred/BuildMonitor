using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class AutoOpenLogSessionTests
{
    [Fact]
    public void Errors_opens_once_when_watch_host_stays_watching_and_rebuild_fails()
    {
        var session = new AutoOpenLogSession();
        var t1 = DateTimeOffset.UtcNow;
        var healthy = Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Green, 0, t1, 0);
        var failed = Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Red, 1, t1.AddSeconds(2), 2);

        Assert.False(session.ShouldOpenViewer(AutoOpenLogMode.Errors, healthy));
        Assert.True(session.ShouldOpenViewer(AutoOpenLogMode.Errors, failed));
        Assert.False(session.ShouldOpenViewer(AutoOpenLogMode.Errors, failed));
    }

    [Fact]
    public void Never_does_not_open_on_watch_rebuild_failure()
    {
        var session = new AutoOpenLogSession();
        var t1 = DateTimeOffset.UtcNow;
        session.ShouldOpenViewer(AutoOpenLogMode.Never, Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Green, 0, t1, 0));

        Assert.False(session.ShouldOpenViewer(
            AutoOpenLogMode.Never,
            Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Red, 1, t1.AddSeconds(2), 2)));
    }

    [Fact]
    public void Successful_watch_rebuild_does_not_open_because_of_old_failure()
    {
        var session = new AutoOpenLogSession();
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(2);
        var t3 = t2.AddSeconds(3);
        session.ShouldOpenViewer(AutoOpenLogMode.Errors, Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Green, 0, t1, 0));
        session.ShouldOpenViewer(AutoOpenLogMode.Errors, Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Red, 1, t2, 2));

        Assert.False(session.ShouldOpenViewer(
            AutoOpenLogMode.Errors,
            Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Green, 0, t3, 0)));
    }

    [Fact]
    public void Second_failed_watch_rebuild_opens_again_when_build_result_changes()
    {
        var session = new AutoOpenLogSession();
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(2);
        var t3 = t2.AddSeconds(4);
        session.ShouldOpenViewer(AutoOpenLogMode.Errors, Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Green, 0, t1, 0));
        Assert.True(session.ShouldOpenViewer(
            AutoOpenLogMode.Errors,
            Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Red, 1, t2, 2)));
        Assert.True(session.ShouldOpenViewer(
            AutoOpenLogMode.Errors,
            Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Red, 1, t3, 2)));
    }

    [Fact]
    public void Direct_build_failed_still_opens_once()
    {
        var session = new AutoOpenLogSession();
        var t1 = DateTimeOffset.UtcNow;
        session.ShouldOpenViewer(
            AutoOpenLogMode.Errors,
            Snapshot(ProjectLifecycleState.Building, MonitorHealth.Green, 0, t1, 0));

        var failed = Snapshot(ProjectLifecycleState.BuildFailed, MonitorHealth.Red, 1, t1.AddSeconds(5), 3);
        Assert.True(session.ShouldOpenViewer(AutoOpenLogMode.Errors, failed));
        Assert.False(session.ShouldOpenViewer(AutoOpenLogMode.Errors, failed));
    }

    [Fact]
    public void Warnings_mode_still_opens_on_health_change_not_on_repeat()
    {
        var session = new AutoOpenLogSession();
        var t1 = DateTimeOffset.UtcNow;
        session.ShouldOpenViewer(
            AutoOpenLogMode.Warnings,
            Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Green, 0, t1, 0, warningCount: 0));

        var warned = Snapshot(ProjectLifecycleState.Watching, MonitorHealth.Amber, 0, t1, 0, warningCount: 4);
        Assert.True(session.ShouldOpenViewer(AutoOpenLogMode.Warnings, warned));
        Assert.False(session.ShouldOpenViewer(AutoOpenLogMode.Warnings, warned));
    }

    private static ProjectHealthSnapshot Snapshot(
        ProjectLifecycleState state,
        MonitorHealth health,
        int lastBuildExitCode,
        DateTimeOffset finishedAt,
        int errorCount,
        int warningCount = 0) =>
        new(
            "p1",
            "Demo",
            health,
            ProjectHealthEvaluator.ToLabel(health),
            state,
            lastBuildExitCode,
            TimeSpan.FromSeconds(1),
            lastBuildExitCode == 0 ? null : "error MSB4018: DefineStaticWebAssets",
            errorCount,
            warningCount,
            DateTimeOffset.UtcNow,
            finishedAt,
            true,
            [],
            LastBuildExitCode: lastBuildExitCode);
}
