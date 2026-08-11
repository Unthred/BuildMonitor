using BuildMonitor.Core.Models;



namespace BuildMonitor.Core.Rules;



public static class StatusPanelBuildVisibilityEvaluator

{

    public static bool ShouldAutoShow(

        bool showWhileBuildingEnabled,

        ProjectLifecycleState previousState,

        ProjectLifecycleState currentState) =>

        showWhileBuildingEnabled

        && currentState == ProjectLifecycleState.Building

        && previousState != ProjectLifecycleState.Building;



    public static bool ShouldAutoHide(

        bool autoShownForBuild,

        IEnumerable<(bool ShowWhileBuildingEnabled, ProjectLifecycleState State)> activeProjects) =>

        autoShownForBuild

        && !activeProjects.Any(p =>

            p.ShowWhileBuildingEnabled && p.State == ProjectLifecycleState.Building);



    public static bool ShouldAutoShowForEditGating(

        bool suppressionEnabled,

        bool isGatingActive,

        bool wasGatingActive) =>

        suppressionEnabled && isGatingActive && !wasGatingActive;



    public static bool ShouldAutoHideForEditGating(

        bool autoShownForEditGating,

        bool isGatingActive) =>

        autoShownForEditGating && !isGatingActive;

    public static bool IsBusyWorkState(ProjectLifecycleState state) =>
        state is ProjectLifecycleState.Building
            or ProjectLifecycleState.WaitingForEdits
            or ProjectLifecycleState.Testing;

    public static bool ShouldAutoShowForBusyWork(
        bool suppressionEnabled,
        bool showStatusPanelWhileBuilding,
        ProjectLifecycleState previousState,
        ProjectLifecycleState currentState)
    {
        if (!suppressionEnabled
            || !IsBusyWorkState(currentState)
            || IsBusyWorkState(previousState))
        {
            return false;
        }

        return currentState switch
        {
            ProjectLifecycleState.Building or ProjectLifecycleState.Testing => showStatusPanelWhileBuilding,
            _ => true
        };
    }

    public static bool ShouldHideWhenBuildStartsWithoutShowSetting(
        bool showStatusPanelWhileBuilding,
        ProjectLifecycleState previousState,
        ProjectLifecycleState currentState,
        bool autoShownForEditGatingOnly) =>
        !showStatusPanelWhileBuilding
        && autoShownForEditGatingOnly
        && currentState == ProjectLifecycleState.Building
        && previousState != ProjectLifecycleState.Building;

    /// <summary>
    /// When the panel was opened for edit-gating, keep it through the following build even if
    /// <c>ShowStatusPanelWhileBuilding</c> is off — otherwise the countdown disappears as work starts.
    /// </summary>
    public static bool ShouldContinueThroughBuildFromEditGating(
        bool showStatusPanelWhileBuilding,
        ProjectLifecycleState previousState,
        ProjectLifecycleState currentState,
        bool autoShownForEditGatingOnly) =>
        ShouldHideWhenBuildStartsWithoutShowSetting(
            showStatusPanelWhileBuilding,
            previousState,
            currentState,
            autoShownForEditGatingOnly);

    public static bool ShouldAutoHideForBusyWork(
        bool autoShown,
        IEnumerable<ProjectLifecycleState> activeStates) =>
        autoShown && !activeStates.Any(IsBusyWorkState);

    public static bool HasPendingRebuild(ProjectHealthSnapshot snapshot) =>
        snapshot.IsEditGatingActive
        || snapshot.RebuildQuietUntilUtc is not null;

    public static bool HasActiveRebuildCountdown(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow) =>
        snapshot.RebuildQuietUntilUtc is not null
        && !string.IsNullOrWhiteSpace(
            EditGatingDetailFormatter.FormatCountdownRemaining(snapshot.RebuildQuietUntilUtc, utcNow));

    public static bool ShouldShowStillEditingButton(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow) =>
        snapshot.State == ProjectLifecycleState.Building
        || HasActiveRebuildCountdown(snapshot, utcNow);

    public static bool StillEditingExtendsQuietPeriod(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow) =>
        snapshot.State != ProjectLifecycleState.Building
        && HasActiveRebuildCountdown(snapshot, utcNow);

    public static string StillEditingToolTip(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow) =>
        StillEditingExtendsQuietPeriod(snapshot, utcNow)
            ? "AI agent still working — extend the rebuild wait"
            : "AI wasn't the cause — mark this build unexpected in Build diagnostics";

    public static bool ShouldShowSiteReady(ProjectHealthSnapshot snapshot) =>
        ShouldShowSiteStatus(snapshot)
        && snapshot.ListenUrlReady
        && !HasPendingRebuild(snapshot);

    public static bool ShouldShowSiteAwaiting(ProjectHealthSnapshot snapshot) =>
        ShouldShowSiteStatus(snapshot)
        && !snapshot.ListenUrlReady;

    public static bool IsAwaitingSiteReady(ProjectHealthSnapshot snapshot) =>
        ShouldShowSiteAwaiting(snapshot);

    public static bool ShouldShowSiteStatus(ProjectHealthSnapshot snapshot) =>
        snapshot.IsActive
        && snapshot.SupportsAppRestart
        && !string.IsNullOrWhiteSpace(snapshot.ListenUrl)
        && snapshot.State is not (ProjectLifecycleState.Building
            or ProjectLifecycleState.Testing
            or ProjectLifecycleState.WaitingForEdits)
        && (snapshot.IsRestarting
            || snapshot.State is ProjectLifecycleState.Running or ProjectLifecycleState.Watching);

    public static bool HasSiteLaunchConfigured(ProjectHealthSnapshot snapshot) =>
        snapshot.SupportsAppRestart && !string.IsNullOrWhiteSpace(snapshot.ListenUrl);

    public static bool ShouldKeepPanelVisibleUntilSiteReady(
        IEnumerable<ProjectHealthSnapshot> activeProjects) =>
        activeProjects.Any(IsAwaitingSiteReady);

    /// <summary>
    /// Blocks auto-dismiss while work is in flight. Edit gating alone does not block once the site is ready.
    /// </summary>
    public static bool ShouldBlockSiteReadyDismiss(ProjectHealthSnapshot snapshot)
    {
        if (snapshot.IsRestarting)
        {
            return true;
        }

        if (snapshot.State is ProjectLifecycleState.Building
            or ProjectLifecycleState.Testing
            or ProjectLifecycleState.WaitingForEdits)
        {
            return true;
        }

        return HasSiteLaunchConfigured(snapshot) && IsAwaitingSiteReady(snapshot);
    }

    /// <summary>
    /// Schedule auto-dismiss when the site banner can show, or the app is already up and only a background rebuild is queued.
    /// </summary>
    public static bool ShouldScheduleSiteReadyDismiss(IEnumerable<ProjectHealthSnapshot> activeProjects)
    {
        var list = activeProjects.ToList();
        if (list.Count == 0 || list.Any(ShouldBlockSiteReadyDismiss))
        {
            return false;
        }

        if (!list.Any(HasSiteLaunchConfigured))
        {
            return true;
        }

        if (list.Any(ShouldShowSiteReady))
        {
            return true;
        }

        return list.All(s =>
            s.State is ProjectLifecycleState.Watching or ProjectLifecycleState.Running
            && HasSiteLaunchConfigured(s)
            && s.ListenUrlReady);
    }
}

