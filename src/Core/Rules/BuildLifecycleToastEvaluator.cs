using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public enum BuildLifecycleToastKind
{
    None = 0,
    BuildStarted = 1,
    BuildSucceeded = 2,
    BuildFailed = 3,
    TestsPassed = 4,
    TestsFailed = 5
}

/// <summary>
/// Pure toast decisions from snapshot transitions. Build failure is a build-result
/// transition so a surviving watch host does not suppress the failure toast.
/// </summary>
public static class BuildLifecycleToastEvaluator
{
    public static BuildLifecycleToastKind Evaluate(
        bool hadPrevious,
        ProjectLifecycleState previousState,
        DateTimeOffset? previousBuildFinishedAtUtc,
        ProjectLifecycleState currentState,
        int currentBuildExitCode,
        DateTimeOffset? currentBuildFinishedAtUtc,
        bool suppressBuildStartedBecauseFileChange)
    {
        if (currentState == ProjectLifecycleState.Building
            && previousState != ProjectLifecycleState.Building
            && !suppressBuildStartedBecauseFileChange)
        {
            return BuildLifecycleToastKind.BuildStarted;
        }

        if (BuildResultTransitionEvaluator.IsNewlyCompletedFailedBuild(
                hadPrevious,
                previousBuildFinishedAtUtc,
                currentBuildExitCode,
                currentBuildFinishedAtUtc))
        {
            return BuildLifecycleToastKind.BuildFailed;
        }

        if (previousState == ProjectLifecycleState.Building
            && BuildLifecycleFormatting.IsSuccessfulBuildEndState(currentState))
        {
            return BuildLifecycleToastKind.BuildSucceeded;
        }

        if (previousState == ProjectLifecycleState.Testing && currentState == ProjectLifecycleState.TestOk)
        {
            return BuildLifecycleToastKind.TestsPassed;
        }

        if (previousState == ProjectLifecycleState.Testing && currentState == ProjectLifecycleState.TestFailed)
        {
            return BuildLifecycleToastKind.TestsFailed;
        }

        return BuildLifecycleToastKind.None;
    }
}
