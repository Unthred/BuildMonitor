using System;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Explicit visibility reasons for the hover status panel. The panel remains visible while
/// any qualifying reason is active; releasing one reason must not hide if another still applies.
/// </summary>
[Flags]
public enum StatusPanelVisibilityReason
{
    None = 0,
    PointerHover = 1,
    LocalBuildActivity = 2,
    AzureBuildActivity = 4,
}

/// <summary>
/// App-level build-activity holds for the status panel (Local and Azure independently).
/// Pointer hover and edit-gating/site-ready flows are orchestrated in the tray app.
/// </summary>
public static class StatusPanelVisibilityPolicy
{
    public static bool IsQualifyingLocalBuildActivity(ProjectHealthSnapshot snapshot) =>
        snapshot.IsActive
        && (snapshot.IsRestarting
            || snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing);

    public static bool IsQualifyingAzureBuildActivity(ProjectHealthSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            return false;
        }

        var azure = snapshot.Azure;
        if (azure is null)
        {
            return false;
        }

        if (azure.Availability is AzureMonitoringAvailability.AuthRequired
            or AzureMonitoringAvailability.Unavailable)
        {
            return false;
        }

        if (azure.CiState == AzureCiMonitoringState.Activity)
        {
            return true;
        }

        return azure.PrimaryRun is not null && AzureRunSelector.IsActive(azure.PrimaryRun.State);
    }

    public static bool HasLocalBuildActivityHold(
        IEnumerable<ProjectHealthSnapshot> activeSnapshots,
        bool keepVisibleDuringLocalBuild) =>
        keepVisibleDuringLocalBuild
        && activeSnapshots.Any(IsQualifyingLocalBuildActivity);

    public static bool HasAzureBuildActivityHold(
        IEnumerable<ProjectHealthSnapshot> activeSnapshots,
        bool keepVisibleDuringAzureBuild) =>
        keepVisibleDuringAzureBuild
        && activeSnapshots.Any(IsQualifyingAzureBuildActivity);

    public static bool HasAnyBuildActivityHold(
        IEnumerable<ProjectHealthSnapshot> activeSnapshots,
        bool keepVisibleDuringLocalBuild,
        bool keepVisibleDuringAzureBuild) =>
        HasLocalBuildActivityHold(activeSnapshots, keepVisibleDuringLocalBuild)
        || HasAzureBuildActivityHold(activeSnapshots, keepVisibleDuringAzureBuild);

    public static StatusPanelVisibilityReason EvaluateBuildActivityReasons(
        IEnumerable<ProjectHealthSnapshot> activeSnapshots,
        bool keepVisibleDuringLocalBuild,
        bool keepVisibleDuringAzureBuild)
    {
        var reasons = StatusPanelVisibilityReason.None;
        if (HasLocalBuildActivityHold(activeSnapshots, keepVisibleDuringLocalBuild))
        {
            reasons |= StatusPanelVisibilityReason.LocalBuildActivity;
        }

        if (HasAzureBuildActivityHold(activeSnapshots, keepVisibleDuringAzureBuild))
        {
            reasons |= StatusPanelVisibilityReason.AzureBuildActivity;
        }

        return reasons;
    }

    public static bool ShouldRemainVisible(StatusPanelVisibilityReason reasons) =>
        reasons != StatusPanelVisibilityReason.None;

    public static bool ShouldSuppressAutoHideForBuildActivity(StatusPanelVisibilityReason reasons) =>
        (reasons & (StatusPanelVisibilityReason.LocalBuildActivity
            | StatusPanelVisibilityReason.AzureBuildActivity)) != 0;
}
