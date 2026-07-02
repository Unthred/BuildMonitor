using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed partial class ProjectRuntime
{
    private AgentActivityWatcher? agentActivityWatcher;
    private bool deferStartupBuildUntilQuiet = true;
    private bool cancelSupersededBuilds = true;
    private bool useAgentTranscriptActivity = true;
    private CancellationTokenSource? buildCancellationSource;
    private string? currentBuildReasonInFlight;

    private BuildSuppressionSettings GetSuppressionSettings() =>
        new(deferStartupBuildUntilQuiet, cancelSupersededBuilds);

    private void ApplyMonitorSuppressionSettings(GlobalMonitorSettings monitor)
    {
        deferStartupBuildUntilQuiet = monitor.DeferStartupBuildUntilQuiet;
        cancelSupersededBuilds = monitor.CancelSupersededBuilds;
        useAgentTranscriptActivity = monitor.UseAgentTranscriptActivity;
    }

    private EditActivitySnapshot EvaluateEditActivity()
    {
        var agentActivity = useAgentTranscriptActivity
                            && agentActivityWatcher is not null
                            && agentActivityWatcher.LastActivityUtc != DateTimeOffset.MinValue
            ? agentActivityWatcher.LastActivityUtc
            : (DateTimeOffset?)null;

        return EditActivitySnapshot.Evaluate(
            new EditActivityInput(
                fileWatcher?.HasPendingChanges == true,
                fileWatcher?.BurstStartedUtc,
                lastMeaningfulFileChangeUtc,
                agentActivity,
                GetSessionAdjustedFileChangeDebounceMs(),
                useAgentTranscriptActivity),
            DateTimeOffset.UtcNow);
    }

    private bool IsEditGatingActive()
    {
        var activity = EvaluateEditActivity();
        return BuildSuppressionPolicy.IsEditGatingActive(
            GetSuppressionSettings(),
            pendingFileChangeRebuild,
            activity,
            pendingRebuildHoldReason);
    }

    private DateTimeOffset? GetEditGatingQuietUntilUtc()
    {
        if (pendingFileChangeRebuild && lastMeaningfulFileChangeUtc != DateTimeOffset.MinValue)
        {
            return AdaptiveFileChangeDebounce.ComputeQuietUntilUtc(
                lastMeaningfulFileChangeUtc,
                GetSessionAdjustedFileChangeDebounceMs());
        }

        var activity = EvaluateEditActivity();
        return activity.IsActive ? activity.QuietUntilUtc : null;
    }

    private string? BuildEditGatingDetailText()
    {
        if (!IsEditGatingActive() && pendingRebuildHoldReason == PendingRebuildHoldReason.None)
        {
            return null;
        }

        PruneRecentFileChangeBuildStarts();
        return EditGatingDetailFormatter.FormatHoldReason(
            pendingRebuildHoldReason,
            pendingRebuildHoldFileCount,
            pendingRebuildHoldSamplePaths,
            pendingRebuildTimerResetCount,
            GetSessionAdjustedFileChangeDebounceMs(),
            recentFileChangeBuildStarts.Count >= 1);
    }

    private void TryStartAgentActivityWatcher()
    {
        if (!useAgentTranscriptActivity || agentActivityWatcher is not null)
        {
            return;
        }

        try
        {
            agentActivityWatcher = new AgentActivityWatcher(definition.RootFolder);
        }
        catch
        {
            agentActivityWatcher = null;
        }
    }

    private void RequestBuildCancellation()
    {
        try
        {
            buildCancellationSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Build already finished.
        }
    }

    private void EnterWaitingForEditsState(string action)
    {
        SetState(ProjectLifecycleState.WaitingForEdits);
        SetProjectCurrentAction(action);
        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(true);
    }
}
