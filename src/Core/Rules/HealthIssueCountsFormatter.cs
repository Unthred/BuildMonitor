using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class HealthIssueCountsFormatter
{
    public static string FormatStatusLine(
        ProjectLifecycleState state,
        int buildErrors,
        int buildWarnings,
        int runErrors,
        int runWarnings)
    {
        if (state is ProjectLifecycleState.Crashed
            or ProjectLifecycleState.Running
            or ProjectLifecycleState.Watching)
        {
            if (runErrors > 0 || runWarnings > 0)
            {
                return $"Run: {runErrors} errors | {runWarnings} warnings";
            }

            if (buildWarnings > 0 && state is not ProjectLifecycleState.Crashed)
            {
                return $"Build: {buildErrors} errors | {buildWarnings} warnings";
            }
        }

        if (state is ProjectLifecycleState.Testing or ProjectLifecycleState.TestFailed)
        {
            return $"Tests: {buildErrors} failed | {buildWarnings} skipped";
        }

        return $"Build: {buildErrors} errors | {buildWarnings} warnings";
    }

    public static (int DisplayErrors, int DisplayWarnings) SelectPrimaryCounts(
        ProjectLifecycleState state,
        int buildErrors,
        int buildWarnings,
        int runErrors,
        int runWarnings) =>
        state is ProjectLifecycleState.Crashed
            or ProjectLifecycleState.Running
            or ProjectLifecycleState.Watching
            && (runErrors > 0 || runWarnings > 0 || state == ProjectLifecycleState.Crashed)
            ? (runErrors, runWarnings)
            : (buildErrors, buildWarnings);

    public static string FormatFailurePhase(ProjectLifecycleState state) =>
        state switch
        {
            ProjectLifecycleState.Crashed => "Run failed",
            ProjectLifecycleState.BuildFailed => "Build failed",
            ProjectLifecycleState.TestFailed => "Tests failed",
            ProjectLifecycleState.Building => "Building",
            ProjectLifecycleState.Testing => "Testing",
            ProjectLifecycleState.Watching => "Watching",
            ProjectLifecycleState.Running => "Running",
            _ => string.Empty
        };
}
