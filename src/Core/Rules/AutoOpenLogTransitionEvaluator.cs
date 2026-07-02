using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class AutoOpenLogTransitionEvaluator
{
    public static bool ShouldOpen(
        AutoOpenLogMode mode,
        MonitorHealth previousHealth,
        MonitorHealth currentHealth,
        ProjectLifecycleState previousState,
        ProjectLifecycleState currentState,
        int errorCount = 0)
    {
        if (mode == AutoOpenLogMode.Never)
        {
            return false;
        }

        if (mode == AutoOpenLogMode.Always)
        {
            return CompletedBuild(previousState, currentState)
                   || CompletedTest(previousState, currentState);
        }

        if (mode == AutoOpenLogMode.Errors)
        {
            if (currentState is ProjectLifecycleState.Building or ProjectLifecycleState.Testing)
            {
                return false;
            }

            if (previousState == ProjectLifecycleState.Building
                && currentState == ProjectLifecycleState.BuildFailed)
            {
                return true;
            }

            if (previousState == ProjectLifecycleState.Testing
                && currentState == ProjectLifecycleState.TestFailed)
            {
                return true;
            }

            return currentState == ProjectLifecycleState.Crashed
                && previousState is ProjectLifecycleState.Running or ProjectLifecycleState.Watching;
        }

        if (mode == AutoOpenLogMode.Warnings)
        {
            return currentHealth is MonitorHealth.Amber or MonitorHealth.Red
                   && previousHealth != currentHealth;
        }

        return false;
    }

    public static (bool SelectErrorsFilter, bool SelectWarningsFilter) ResolveIssueFilters(
        AutoOpenLogMode mode,
        ProjectHealthSnapshot snapshot)
    {
        if (mode == AutoOpenLogMode.Warnings
            && snapshot.Health == MonitorHealth.Amber
            && snapshot.ErrorCount == 0
            && snapshot.WarningCount > 0)
        {
            return (false, true);
        }

        if (mode == AutoOpenLogMode.Errors
            || snapshot.State is ProjectLifecycleState.BuildFailed
                or ProjectLifecycleState.TestFailed
                or ProjectLifecycleState.Crashed
            || snapshot.ErrorCount > 0)
        {
            return (true, false);
        }

        return (false, false);
    }

    public static bool ShouldResetOpenLatch(AutoOpenLogMode mode, MonitorHealth currentHealth) =>
        mode switch
        {
            AutoOpenLogMode.Never => true,
            AutoOpenLogMode.Errors => currentHealth != MonitorHealth.Red,
            AutoOpenLogMode.Warnings => currentHealth is MonitorHealth.Green or MonitorHealth.Unknown,
            AutoOpenLogMode.Always => false,
            _ => true
        };

    public static bool PreferErrorsFilter(int errorCount, int warningCount) => errorCount > 0;

    public static bool PreferWarningsFilter(int errorCount, int warningCount) =>
        errorCount == 0 && warningCount > 0;

    private static bool CompletedBuild(ProjectLifecycleState previous, ProjectLifecycleState current) =>
        previous == ProjectLifecycleState.Building
        && current is not ProjectLifecycleState.Building;

    private static bool CompletedTest(ProjectLifecycleState previous, ProjectLifecycleState current) =>
        previous == ProjectLifecycleState.Testing
        && current is ProjectLifecycleState.TestOk or ProjectLifecycleState.TestFailed;
}
