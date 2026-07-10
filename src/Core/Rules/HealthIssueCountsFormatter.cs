using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class HealthIssueCountsFormatter
{
    public static string? FormatStatusLine(
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

            if (state is not ProjectLifecycleState.Crashed
                && (buildErrors > 0 || buildWarnings > 0))
            {
                return $"Build: {buildErrors} errors | {buildWarnings} warnings";
            }

            return null;
        }

        if (state is ProjectLifecycleState.Testing or ProjectLifecycleState.TestFailed)
        {
            return $"Tests: {buildErrors} failed | {buildWarnings} skipped";
        }

        if (buildErrors == 0 && buildWarnings == 0)
        {
            return null;
        }

        return $"Build: {buildErrors} errors | {buildWarnings} warnings";
    }

    public static (int DisplayErrors, int DisplayWarnings) SelectPrimaryCounts(
        ProjectLifecycleState state,
        int buildErrors,
        int buildWarnings,
        int runErrors,
        int runWarnings)
    {
        var isRunPhase = state is ProjectLifecycleState.Crashed
            or ProjectLifecycleState.Running
            or ProjectLifecycleState.Watching;

        if (isRunPhase && (runErrors > 0 || runWarnings > 0 || state == ProjectLifecycleState.Crashed))
        {
            return (runErrors, runWarnings);
        }

        return (buildErrors, buildWarnings);
    }

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
