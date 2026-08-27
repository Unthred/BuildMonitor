using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Capability flags for Projects Settings presentation. Derived from attachment + run mode +
/// launch-profile evidence — never from project display names.
/// </summary>
public sealed record SettingsProjectCapabilities(
    bool HasLocalAttachment,
    bool RunModeNone,
    bool Runnable,
    bool LaunchProfilesAvailable,
    bool SiteUrlApplicable,
    bool WatchApplicable,
    bool RestartApplicable,
    bool WatchRestartApplicable,
    bool TestsApplicable);

/// <summary>Computes which Settings controls are meaningful for the selected project.</summary>
public static class SettingsProjectCapabilityPolicy
{
    public static SettingsProjectCapabilities Evaluate(
        MonitoredProjectSettings? project,
        bool launchProfilesAvailable = false,
        bool siteUrlApplicable = false)
    {
        var local = project?.Local;
        if (local is null)
        {
            return new SettingsProjectCapabilities(
                HasLocalAttachment: false,
                RunModeNone: true,
                Runnable: false,
                LaunchProfilesAvailable: false,
                SiteUrlApplicable: false,
                WatchApplicable: false,
                RestartApplicable: false,
                WatchRestartApplicable: false,
                TestsApplicable: false);
        }

        var runMode = local.RunOptions.RunMode;
        var runModeNone = runMode == ProjectRunMode.None;
        var runnable = !runModeNone;
        var watch = runMode == ProjectRunMode.Watch;

        return new SettingsProjectCapabilities(
            HasLocalAttachment: true,
            RunModeNone: runModeNone,
            Runnable: runnable,
            LaunchProfilesAvailable: runnable && launchProfilesAvailable,
            SiteUrlApplicable: runnable && siteUrlApplicable,
            WatchApplicable: true,
            RestartApplicable: runnable,
            WatchRestartApplicable: watch,
            TestsApplicable: true);
    }
}
