using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Capability-driven status-card toolbar visibility. Independent of health / failure styling.
/// </summary>
public static class StatusPanelCardActionRules
{
    /// <summary>
    /// Explicit rebuild is always available for an active Local project (control-plane / tray rebuild).
    /// Does not require a supervised run host.
    /// </summary>
    public static bool ShowRebuild(ProjectHealthSnapshot snapshot) =>
        snapshot.IsActive;

    /// <summary>Restart run/watch without rebuild — requires a supervised host (<see cref="ProjectHealthSnapshot.SupportsAppRestart"/>).</summary>
    public static bool ShowRestart(ProjectHealthSnapshot snapshot) =>
        snapshot.IsActive && snapshot.SupportsAppRestart;

    /// <summary>Rebuild then start run/watch — requires rebuild + supervised host.</summary>
    public static bool ShowRebuildAndRestart(ProjectHealthSnapshot snapshot) =>
        ShowRebuild(snapshot) && ShowRestart(snapshot);

    /// <summary>Manual test run — always offered for active Local projects.</summary>
    public static bool ShowRunTests(ProjectHealthSnapshot snapshot) =>
        snapshot.IsActive;
}
