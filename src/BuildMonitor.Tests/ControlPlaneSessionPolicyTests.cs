using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneSessionPolicyTests
{
    [Fact]
    public void ResolveEffectiveState_expires_busy_after_timeout()
    {
        var since = DateTimeOffset.Parse("2026-08-12T08:00:00Z");
        var now = since.AddMinutes(3);
        var state = ControlPlaneSessionPolicy.ResolveEffectiveState(
            ControlPlaneSessionState.Busy,
            since,
            busyTimeoutSeconds: 120,
            now);
        Assert.Equal(ControlPlaneSessionState.Idle, state);
    }

    [Fact]
    public void ResolveEffectiveState_keeps_busy_within_timeout()
    {
        var since = DateTimeOffset.Parse("2026-08-12T08:00:00Z");
        var now = since.AddSeconds(30);
        var state = ControlPlaneSessionPolicy.ResolveEffectiveState(
            ControlPlaneSessionState.Busy,
            since,
            busyTimeoutSeconds: 120,
            now);
        Assert.Equal(ControlPlaneSessionState.Busy, state);
    }

    [Fact]
    public void ShouldBlockAutoBuild_only_when_session_api_used_and_busy()
    {
        Assert.False(ControlPlaneSessionPolicy.ShouldBlockAutoBuild(false, ControlPlaneSessionState.Busy));
        Assert.False(ControlPlaneSessionPolicy.ShouldBlockAutoBuild(true, ControlPlaneSessionState.Idle));
        Assert.True(ControlPlaneSessionPolicy.ShouldBlockAutoBuild(true, ControlPlaneSessionState.Busy));
    }

    [Fact]
    public void ResolveSuppressAutoBuildTests_prefers_override()
    {
        Assert.False(ControlPlaneSessionPolicy.ResolveSuppressAutoBuildTests(false, settingsDefault: true));
        Assert.True(ControlPlaneSessionPolicy.ResolveSuppressAutoBuildTests(null, settingsDefault: true));
    }
}
