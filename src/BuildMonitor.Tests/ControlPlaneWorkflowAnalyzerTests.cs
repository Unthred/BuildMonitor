using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneWorkflowAnalyzerTests
{
    [Fact]
    public void No_session_api_reports_waiting()
    {
        var snapshot = ControlPlaneWorkflowAnalyzer.Analyze(
            "p1",
            session: new ControlPlaneSessionStatus(
                ControlPlaneSessionState.Idle,
                DateTimeOffset.UtcNow,
                SessionApiUsed: false,
                SuppressAutoBuildTests: true),
            [],
            [],
            buildsBlockedToday: 0,
            DateTimeOffset.UtcNow);

        Assert.Equal(ControlPlaneWorkflowHealth.NoSessionApi, snapshot.Health);
    }

    [Fact]
    public void Busy_with_blocked_builds_reports_held()
    {
        var now = DateTimeOffset.UtcNow;
        var events = new List<ControlPlaneEventRecord>
        {
            new("1", "p1", now.AddMinutes(-1), ControlPlaneEventKind.Busy, "Agent busy"),
            new("2", "p1", now.AddSeconds(-30), ControlPlaneEventKind.BuildBlocked, "File change held")
        };

        var snapshot = ControlPlaneWorkflowAnalyzer.Analyze(
            "p1",
            new ControlPlaneSessionStatus(
                ControlPlaneSessionState.Busy,
                now.AddMinutes(-1),
                SessionApiUsed: true,
                SuppressAutoBuildTests: true),
            events,
            [],
            buildsBlockedToday: 3,
            now);

        Assert.Equal(ControlPlaneWorkflowHealth.Busy, snapshot.Health);
        Assert.Contains("3", snapshot.StatusDetail);
    }

    [Fact]
    public void One_build_after_idle_is_healthy()
    {
        var now = DateTimeOffset.UtcNow;
        var idleAt = now.AddMinutes(-2);
        var busyAt = idleAt.AddMinutes(-1);
        var events = new List<ControlPlaneEventRecord>
        {
            new("1", "p1", idleAt, ControlPlaneEventKind.IdleAgent, "Agent idle"),
            new("2", "p1", busyAt, ControlPlaneEventKind.Busy, "Agent busy")
        };
        var builds = new List<BuildTriggerRecord>
        {
            new("b1", "p1", "Demo", idleAt.AddSeconds(8), BuildTriggerKind.FileWatcher, "file change")
        };

        var snapshot = ControlPlaneWorkflowAnalyzer.Analyze(
            "p1",
            new ControlPlaneSessionStatus(
                ControlPlaneSessionState.Idle,
                idleAt,
                SessionApiUsed: true,
                SuppressAutoBuildTests: true,
                IdleCause: ControlPlaneIdleCause.Agent),
            events,
            builds,
            buildsBlockedToday: 2,
            now);

        Assert.Equal(ControlPlaneWorkflowHealth.Healthy, snapshot.Health);
        Assert.Equal(1, snapshot.BuildsAfterLastIdle);
        Assert.Contains("1 build", snapshot.LastCycleSummary);
    }

    [Fact]
    public void Multiple_builds_after_idle_flags_extra()
    {
        var now = DateTimeOffset.UtcNow;
        var idleAt = now.AddMinutes(-1);
        var events = new List<ControlPlaneEventRecord>
        {
            new("1", "p1", idleAt, ControlPlaneEventKind.IdleAgent, "Agent idle"),
            new("2", "p1", idleAt.AddMinutes(-2), ControlPlaneEventKind.Busy, "Agent busy")
        };
        var builds = new List<BuildTriggerRecord>
        {
            new("b1", "p1", "Demo", idleAt.AddSeconds(10), BuildTriggerKind.FileWatcher, "file change"),
            new("b2", "p1", "Demo", idleAt.AddSeconds(40), BuildTriggerKind.FileWatcher, "file change")
        };

        var snapshot = ControlPlaneWorkflowAnalyzer.Analyze(
            "p1",
            new ControlPlaneSessionStatus(
                ControlPlaneSessionState.Idle,
                idleAt,
                SessionApiUsed: true,
                SuppressAutoBuildTests: true,
                IdleCause: ControlPlaneIdleCause.Agent),
            events,
            builds,
            buildsBlockedToday: 0,
            now);

        Assert.Equal(ControlPlaneWorkflowHealth.ExtraBuilds, snapshot.Health);
        Assert.Equal(2, snapshot.BuildsAfterLastIdle);
    }
}
