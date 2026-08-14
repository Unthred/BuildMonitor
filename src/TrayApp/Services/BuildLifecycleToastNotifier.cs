using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Emits build/test lifecycle toasts from health snapshot transitions.
/// </summary>
public sealed class BuildLifecycleToastNotifier
{
    private readonly Dictionary<string, ObservedBuild> previousByProject =
        new(StringComparer.OrdinalIgnoreCase);

    public void Reset() => previousByProject.Clear();

    public void Process(
        IReadOnlyList<ProjectHealthSnapshot> snapshots,
        ISet<string> fileChangeBuildStarts)
    {
        foreach (var snapshot in snapshots.Where(s => s.IsActive))
        {
            var hadPrevious = previousByProject.TryGetValue(snapshot.ProjectId, out var previous);
            var previousState = hadPrevious ? previous.State : ProjectLifecycleState.Idle;
            var suppressStart = false;
            if (snapshot.State == ProjectLifecycleState.Building
                && previousState != ProjectLifecycleState.Building)
            {
                suppressStart = fileChangeBuildStarts.Remove(snapshot.ProjectId);
            }

            var kind = BuildLifecycleToastEvaluator.Evaluate(
                hadPrevious,
                previousState,
                hadPrevious ? previous.LastBuildFinishedAtUtc : null,
                snapshot.State,
                snapshot.LastBuildExitCode,
                snapshot.LastBuildFinishedAtUtc,
                suppressStart);

            Show(kind, snapshot);

            previousByProject[snapshot.ProjectId] = new ObservedBuild(
                snapshot.State,
                snapshot.LastBuildExitCode,
                snapshot.LastBuildFinishedAtUtc);
        }

        var activeIds = snapshots.Where(s => s.IsActive).Select(s => s.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in previousByProject.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            previousByProject.Remove(staleId);
        }
    }

    private static void Show(BuildLifecycleToastKind kind, ProjectHealthSnapshot snapshot)
    {
        switch (kind)
        {
            case BuildLifecycleToastKind.BuildStarted:
                ToastNotificationService.ShowIfEnabled(
                    $"Building — {snapshot.DisplayName}",
                    "Build started.",
                    ToastKind.Info,
                    UserNotificationCategory.BuildStart);
                break;
            case BuildLifecycleToastKind.BuildSucceeded:
                var successMessage = snapshot.LastDuration is { } duration
                    ? $"Completed in {BuildLifecycleFormatting.FormatBuildDuration(duration)}."
                    : "Build completed successfully.";
                ToastNotificationService.ShowIfEnabled(
                    $"Build succeeded — {snapshot.DisplayName}",
                    successMessage,
                    ToastKind.Success,
                    UserNotificationCategory.BuildSuccess);
                break;
            case BuildLifecycleToastKind.BuildFailed:
                ToastNotificationService.ShowIfEnabled(
                    $"Build failed — {snapshot.DisplayName}",
                    string.IsNullOrWhiteSpace(snapshot.LastErrorPreview)
                        ? "See build log for details."
                        : snapshot.LastErrorPreview,
                    ToastKind.Error,
                    UserNotificationCategory.BuildFailure);
                break;
            case BuildLifecycleToastKind.TestsPassed:
                ToastNotificationService.ShowIfEnabled(
                    $"Tests passed — {snapshot.DisplayName}",
                    "Tests completed successfully.",
                    ToastKind.Success,
                    UserNotificationCategory.BuildSuccess);
                break;
            case BuildLifecycleToastKind.TestsFailed:
                ToastNotificationService.ShowIfEnabled(
                    $"Tests failed — {snapshot.DisplayName}",
                    string.IsNullOrWhiteSpace(snapshot.LastErrorPreview)
                        ? "See test log for details."
                        : snapshot.LastErrorPreview,
                    ToastKind.Error,
                    UserNotificationCategory.BuildFailure);
                break;
        }
    }

    private readonly record struct ObservedBuild(
        ProjectLifecycleState State,
        int LastBuildExitCode,
        DateTimeOffset? LastBuildFinishedAtUtc);
}
