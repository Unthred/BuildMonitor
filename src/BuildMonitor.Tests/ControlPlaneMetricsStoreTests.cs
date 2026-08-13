using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneMetricsStoreTests
{
    [Fact]
    public void Records_http_busy_idle_and_call_rate()
    {
        var store = new ControlPlaneMetricsStore();
        store.RecordHttp("p1", "session/busy", 200);
        store.RecordHttp("p1", "session/idle", 200);
        store.RecordHttp("p1", "session/busy", 400);

        var snap = store.GetSnapshot("p1");
        Assert.Equal(1, snap.BusyCalls);
        Assert.Equal(1, snap.IdleCalls);
        Assert.Equal(3, snap.HttpRequests);
        Assert.Equal(1, snap.HttpClientErrors);
        Assert.Equal(3, snap.CallsLastHour);
    }

    [Fact]
    public void Ship_check_success_rate_and_average_duration()
    {
        var store = new ControlPlaneMetricsStore();
        store.RecordShipCheck("p1", ok: true, TimeSpan.FromSeconds(2));
        store.RecordShipCheck("p1", ok: false, TimeSpan.FromSeconds(4));

        var snap = store.GetSnapshot("p1");
        Assert.Equal(2, snap.ShipCheckTotal);
        Assert.Equal(1, snap.ShipCheckPassed);
        Assert.Equal(1, snap.ShipCheckFailed);
        Assert.Equal("50%", snap.ShipCheckSuccessRateText);
        Assert.Contains("s", snap.AvgShipCheckDurationText);
    }

    [Fact]
    public void Busy_interval_accumulates()
    {
        var store = new ControlPlaneMetricsStore();
        store.RecordBusyInterval("p1", TimeSpan.FromSeconds(90));
        var snap = store.GetSnapshot("p1");
        Assert.Contains("min", snap.TotalBusyTimeText);
    }
}
