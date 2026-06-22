using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Emits build/test lifecycle toasts from health snapshot transitions.
/// </summary>
public sealed class BuildLifecycleToastNotifier
{
    private readonly Dictionary<string, ProjectLifecycleState> previousProjectState =
        new(StringComparer.OrdinalIgnoreCase);

    public void Reset() => previousProjectState.Clear();

    public void Process(
        IReadOnlyList<ProjectHealthSnapshot> snapshots,
        ISet<string> fileChangeBuildStarts)
    {
        foreach (var snapshot in snapshots.Where(s => s.IsActive))
        {
            previousProjectState.TryGetValue(snapshot.ProjectId, out var previousState);
            var currentState = snapshot.State;

            if (currentState == ProjectLifecycleState.Building && previousState != ProjectLifecycleState.Building)
            {
                if (!fileChangeBuildStarts.Remove(snapshot.ProjectId))
                {
                    ToastNotificationService.ShowIfEnabled(
                        $"Building — {snapshot.DisplayName}",
                        "Build started.",
                        ToastKind.Info,
                        UserNotificationCategory.BuildStart);
                }
            }

            if (previousState == ProjectLifecycleState.Building
                && BuildLifecycleFormatting.IsSuccessfulBuildEndState(currentState))
            {
                var message = snapshot.LastDuration is { } duration
                    ? $"Completed in {BuildLifecycleFormatting.FormatBuildDuration(duration)}."
                    : "Build completed successfully.";
                ToastNotificationService.ShowIfEnabled(
                    $"Build succeeded — {snapshot.DisplayName}",
                    message,
                    ToastKind.Success,
                    UserNotificationCategory.BuildSuccess);
            }
            else if (previousState == ProjectLifecycleState.Testing && currentState == ProjectLifecycleState.TestOk)
            {
                ToastNotificationService.ShowIfEnabled(
                    $"Tests passed — {snapshot.DisplayName}",
                    "Tests completed successfully.",
                    ToastKind.Success,
                    UserNotificationCategory.BuildSuccess);
            }

            if ((previousState == ProjectLifecycleState.Building
                    || previousState == ProjectLifecycleState.Watching)
                && currentState == ProjectLifecycleState.BuildFailed)
            {
                var message = string.IsNullOrWhiteSpace(snapshot.LastErrorPreview)
                    ? "See build log for details."
                    : snapshot.LastErrorPreview;
                ToastNotificationService.ShowIfEnabled(
                    $"Build failed — {snapshot.DisplayName}",
                    message,
                    ToastKind.Error,
                    UserNotificationCategory.BuildFailure);
            }
            else if (previousState == ProjectLifecycleState.Testing && currentState == ProjectLifecycleState.TestFailed)
            {
                var message = string.IsNullOrWhiteSpace(snapshot.LastErrorPreview)
                    ? "See test log for details."
                    : snapshot.LastErrorPreview;
                ToastNotificationService.ShowIfEnabled(
                    $"Tests failed — {snapshot.DisplayName}",
                    message,
                    ToastKind.Error,
                    UserNotificationCategory.BuildFailure);
            }

            previousProjectState[snapshot.ProjectId] = currentState;
        }

        var activeIds = snapshots.Where(s => s.IsActive).Select(s => s.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in previousProjectState.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            previousProjectState.Remove(staleId);
        }
    }
}
