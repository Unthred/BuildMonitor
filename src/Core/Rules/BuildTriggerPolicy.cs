using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Central policy: whether a file-change observation may schedule automatic build work.
/// Observe ≠ schedule — AI Controlled always observes but never auto-schedules.
/// </summary>
public static class BuildTriggerPolicy
{
    public static bool ShouldAutoBuildFromFileChange(
        ProjectBuildControlMode mode,
        bool sessionApiUsed,
        ControlPlaneSessionState effectiveSessionState)
    {
        if (mode == ProjectBuildControlMode.AiControlled)
        {
            return false;
        }

        return !ControlPlaneSessionPolicy.ShouldBlockAutoBuild(sessionApiUsed, effectiveSessionState);
    }

    /// <summary>
    /// True when file-change auto-build is fundamentally disabled by mode
    /// (not merely temporarily held by busy session).
    /// </summary>
    public static bool IsAutoBuildDisabledByMode(ProjectBuildControlMode mode) =>
        mode == ProjectBuildControlMode.AiControlled;

    /// <summary>
    /// Busy timeout may resume auto-build only in File Watching mode.
    /// </summary>
    public static bool BusyTimeoutMayResumeAutoBuild(ProjectBuildControlMode mode) =>
        mode == ProjectBuildControlMode.FileWatching;
}
