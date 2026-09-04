using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
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
        if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode))
        {
            // Pending observed changes are not a "quiet countdown" — no edit-gating UI.
            return false;
        }

        var activity = EvaluateEditActivity();
        return BuildSuppressionPolicy.IsEditGatingActive(
            GetSuppressionSettings(),
            pendingFileChangeRebuild,
            activity,
            pendingRebuildHoldReason);
    }

    private DateTimeOffset? GetEditGatingQuietUntilUtc()
    {
        if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode))
        {
            return null;
        }

        var activity = EvaluateEditActivity();
        return EditGatingQuietUntilResolver.Resolve(
            pendingFileChangeRebuild,
            lastMeaningfulFileChangeUtc,
            GetSessionAdjustedFileChangeDebounceMs(),
            activity);
    }

    private DateTimeOffset GetEffectiveEditQuietUntilUtc()
    {
        var activity = EvaluateEditActivity();
        return EditGatingQuietUntilResolver.Resolve(
                   pendingFileChangeRebuild,
                   lastMeaningfulFileChangeUtc,
                   GetSessionAdjustedFileChangeDebounceMs(),
                   activity)
               ?? DateTimeOffset.UtcNow;
    }

    private string? BuildEditGatingDetailText()
    {
        if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode))
        {
            return null;
        }

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
            agentActivityWatcher = new AgentActivityWatcher(Local.RootFolder);
            agentActivityWatcher.ActivityDetected += OnAgentActivityDetected;
        }
        catch
        {
            agentActivityWatcher = null;
        }
    }

    private void OnAgentActivityDetected()
    {
        // Keep countdown in sync without an immediate UI flood on every .cursor write.
        // Lifecycle transitions still use immediate coalesce via SetState / file watcher.
        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(false);
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
        history.RecordWaitingForEditsEntered(action);
        SetState(ProjectLifecycleState.WaitingForEdits);
        SetProjectCurrentAction(action);
        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(true);
    }

    public StillEditingClickResult HandleStillEditingClick()
    {
        if (Volatile.Read(ref buildInProgress) != 0)
        {
            return MarkInFlightBuildUnexpected()
                ? StillEditingClickResult.BuildMarkedUnexpected
                : StillEditingClickResult.NotApplicable;
        }

        return ExtendRebuildQuietPeriod()
            ? StillEditingClickResult.QuietPeriodExtended
            : StillEditingClickResult.NotApplicable;
    }

    public bool MarkInFlightBuildUnexpected()
    {
        if (Volatile.Read(ref buildInProgress) == 0 || string.IsNullOrWhiteSpace(currentBuildTriggerId))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var note = InFlightBuildUnexpectedNoteFormatter.Format(EvaluateEditActivity(), now);
        triggerJournal.SetVerdict(currentBuildTriggerId, BuildTriggerVerdict.Unexpected);
        triggerJournal.SetUserNote(currentBuildTriggerId, note);
        if (learnFromDiagnosticsVerdicts && Volatile.Read(ref buildTriggeredByFileChange) != 0)
        {
            trainingStore.RecordUnexpectedVerdict(projectSettings.Id);
            burstStatsStore.RecordUnexpectedVerdict(projectSettings.Id);
            SyncFileWatcherDebounceMs();
        }

        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(true);
        return true;
    }

    private bool ExtendRebuildQuietPeriod()
    {
        if (Volatile.Read(ref buildInProgress) != 0)
        {
            return false;
        }

        if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (GetEditGatingQuietUntilUtc() is not { } until || until <= now)
        {
            return false;
        }

        lastMeaningfulFileChangeUtc = now;
        pendingFileChangeRebuild = true;
        var buildReason = string.Equals(pendingBuildReason, "startup", StringComparison.OrdinalIgnoreCase)
            ? "startup"
            : "file change (queued)";
        pendingRebuildHoldReason = PendingRebuildHoldReason.EditsStillArriving;
        pendingRebuildTimerResetCount++;

        EnterWaitingForEditsState("Waiting for edits to settle… (extended)");
        Interlocked.Increment(ref fileChangeRebuildScheduleGeneration);
        _ = WaitForEditQuietThenBuildAsync(buildReason);
        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(true);
        return true;
    }
}
