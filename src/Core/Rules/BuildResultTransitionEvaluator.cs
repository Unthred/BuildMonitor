using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Detects a newly completed build result. Lifecycle may stay <see cref="ProjectLifecycleState.Watching"/>
/// while the watch host remains alive; callers must key off the build result, not only state.
/// </summary>
public static class BuildResultTransitionEvaluator
{
    public static bool IsNewlyCompletedFailedBuild(
        bool hadPrevious,
        DateTimeOffset? previousFinishedAtUtc,
        int currentExitCode,
        DateTimeOffset? currentFinishedAtUtc)
    {
        if (!hadPrevious || currentFinishedAtUtc is null)
        {
            return false;
        }

        if (!HealthIssueCountsFormatter.HasFailedCurrentBuild(currentExitCode))
        {
            return false;
        }

        return previousFinishedAtUtc != currentFinishedAtUtc;
    }

    public static bool IsNewlyCompletedSuccessfulBuild(
        bool hadPrevious,
        DateTimeOffset? previousFinishedAtUtc,
        int currentExitCode,
        DateTimeOffset? currentFinishedAtUtc)
    {
        if (!hadPrevious || currentFinishedAtUtc is null || currentExitCode != 0)
        {
            return false;
        }

        return previousFinishedAtUtc != currentFinishedAtUtc;
    }
}
