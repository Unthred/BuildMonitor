using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class ControlPlaneSessionPolicy
{
    public static ControlPlaneSessionState ResolveEffectiveState(
        ControlPlaneSessionState state,
        DateTimeOffset sinceUtc,
        int busyTimeoutSeconds,
        DateTimeOffset utcNow)
    {
        if (state != ControlPlaneSessionState.Busy)
        {
            return ControlPlaneSessionState.Idle;
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(busyTimeoutSeconds, 30, 3600));
        return utcNow - sinceUtc >= timeout
            ? ControlPlaneSessionState.Idle
            : ControlPlaneSessionState.Busy;
    }

    /// <summary>
    /// Once the session API has been used for a project this process lifetime,
    /// auto-build is blocked while effective state is busy.
    /// </summary>
    public static bool ShouldBlockAutoBuild(bool sessionApiUsed, ControlPlaneSessionState effectiveState) =>
        sessionApiUsed && effectiveState == ControlPlaneSessionState.Busy;

    public static bool ResolveSuppressAutoBuildTests(bool? perProjectOverride, bool settingsDefault) =>
        perProjectOverride ?? settingsDefault;
}
