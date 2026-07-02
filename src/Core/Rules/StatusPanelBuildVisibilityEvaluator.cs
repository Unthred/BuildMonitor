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

    public static bool ShouldAutoHideForBusyWork(
        bool autoShown,
        IEnumerable<ProjectLifecycleState> activeStates) =>
        autoShown && !activeStates.Any(IsBusyWorkState);

    public static bool IsAwaitingSiteReady(ProjectHealthSnapshot snapshot) =>
        ShouldShowSiteStatus(snapshot)
        && !snapshot.ListenUrlReady;

    public static bool ShouldShowSiteStatus(ProjectHealthSnapshot snapshot) =>
        snapshot.IsActive
        && snapshot.SupportsAppRestart
        && !string.IsNullOrWhiteSpace(snapshot.ListenUrl)
        && (snapshot.IsRestarting
            || snapshot.State is ProjectLifecycleState.Running or ProjectLifecycleState.Watching);

    public static bool HasSiteLaunchConfigured(ProjectHealthSnapshot snapshot) =>
        snapshot.SupportsAppRestart && !string.IsNullOrWhiteSpace(snapshot.ListenUrl);

    public static bool ShouldKeepPanelVisibleUntilSiteReady(
        IEnumerable<ProjectHealthSnapshot> activeProjects) =>
        activeProjects.Any(IsAwaitingSiteReady);
}

