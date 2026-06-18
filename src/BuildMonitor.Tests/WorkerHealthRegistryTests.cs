using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Tests;

public sealed class WorkerHealthRegistryTests
{
    [Fact]
    public void Heartbeat_within_stale_window_reports_ok()
    {
        var registry = new WorkerHealthRegistry();
        registry.Register("test.worker", "Test worker", TimeSpan.FromSeconds(5));

        registry.Heartbeat("test.worker", note: "tick");

        var snapshot = registry.GetSnapshots().Single();
        Assert.Equal(WorkerHealthState.Ok, snapshot.State);
        Assert.Equal(1, snapshot.HeartbeatCount);
        Assert.Equal("tick", snapshot.LastNote);
    }

    [Fact]
    public void Missing_heartbeat_past_stale_window_reports_stale()
    {
        var registry = new WorkerHealthRegistry();
        registry.Register("test.worker", "Test worker", TimeSpan.FromMilliseconds(50));
        registry.Heartbeat("test.worker");

        var snapshot = registry.GetSnapshots(DateTimeOffset.UtcNow.AddSeconds(1)).Single();
        Assert.Equal(WorkerHealthState.Stale, snapshot.State);
    }

    [Fact]
    public void SetCurrentAction_appears_on_snapshot()
    {
        var registry = new WorkerHealthRegistry();
        registry.Register("test.worker", "Test worker", TimeSpan.FromSeconds(5));
        registry.SetCurrentAction("test.worker", "Building — startup");

        var snapshot = registry.GetSnapshots().Single();
        Assert.Equal("Building — startup", snapshot.CurrentAction);
    }

    [Fact]
    public void RecordTimeout_marks_worker_unresponsive_when_stale()
    {
        var registry = new WorkerHealthRegistry();
        registry.Register("ui.dispatcher", "UI dispatcher", TimeSpan.FromMilliseconds(50));
        registry.Heartbeat("ui.dispatcher");
        registry.RecordTimeout("ui.dispatcher");

        var snapshot = registry.GetSnapshots(DateTimeOffset.UtcNow.AddSeconds(1)).Single();
        Assert.Equal(WorkerHealthState.Unresponsive, snapshot.State);
        Assert.Equal(1, snapshot.TimeoutCount);
    }
}
