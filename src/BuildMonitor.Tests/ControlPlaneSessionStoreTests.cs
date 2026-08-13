using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneSessionStoreTests
{
    [Fact]
    public void MarkBusy_then_timeout_unblocks_auto_build()
    {
        var store = new ControlPlaneSessionStore();
        store.ApplyMonitorDefaults(busyTimeoutSeconds: 60, suppressAutoBuildTestsDefault: true);
        store.MarkBusy("p1");
        Assert.True(store.ShouldBlockAutoBuild("p1"));

        var status = store.GetStatus("p1", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Equal(ControlPlaneSessionState.Idle, status.State);
        Assert.False(ControlPlaneSessionPolicy.ShouldBlockAutoBuild(status.SessionApiUsed, status.State));
    }

    [Fact]
    public void Suppress_override_wins_over_settings_default()
    {
        var store = new ControlPlaneSessionStore();
        store.ApplyMonitorDefaults(120, suppressAutoBuildTestsDefault: true);
        store.SetSuppressAutoBuildTests("p1", false);
        Assert.False(store.ShouldSuppressAutoBuildTests("p1"));
    }

    [Fact]
    public void Until_session_api_used_auto_build_is_not_blocked()
    {
        var store = new ControlPlaneSessionStore();
        store.ApplyMonitorDefaults(120, true);
        Assert.False(store.ShouldBlockAutoBuild("unknown"));
    }
}
