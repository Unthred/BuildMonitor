using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed partial class ProjectRuntime
{
    private void TryStartFileWatcher()
    {
        if (!ShouldStartFileWatcher())
        {
            return;
        }

        try
        {
            fileChangeDebounceMs = ResolveFileChangeDebounceMs();
            fileWatcher = new DebouncedFileWatcher(
                Local.RootFolder,
                fileChangeDebounceMs,
                GetEffectiveWatchIgnoreSegments());
            fileWatcher.Changed += OnFileWatcherChanged;
            Interlocked.Increment(ref watcherCreateCount);
        }
        catch (Exception ex)
        {
            notifyUser?.Invoke(
                projectSettings.Id,
                $"File watcher disabled — {projectSettings.DisplayName}",
                $"Could not watch '{Local.RootFolder}': {ex.Message}",
                UserNotificationKind.Warning,
                UserNotificationCategory.Warning);
        }
    }

    private void BeginRebuildDisplayReset()
    {
        lock (liveOutputSync)
        {
            liveBuildOutput.Clear();
        }

        watchRebuildInProgress = false;
        Interlocked.Exchange(ref liveOutputRevision, 0);
        lastErrorPreview = null;
        buildProgressTracker = new BuildProgressTracker();
        buildProgressTracker.Reset();
        progressSteps = buildProgressTracker.Steps;
        SetState(ProjectLifecycleState.Building);
        SetProjectCurrentAction($"Building — {pendingBuildReason}");
        NotifyProgressChanged(force: true);
        RequestHealthCoalesce(immediate: true);
    }

    public async Task BuildAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref buildAsyncInvocationCount);
        if (Interlocked.CompareExchange(ref buildInProgress, 1, 0) != 0)
        {
            // Rejected rebuild: force a user-visible refresh now so the status panel
            // can't lag behind live build activity.
            RequestHealthCoalesce(immediate: true);
            return;
        }

        var triggeredByFileChange = Volatile.Read(ref buildTriggeredByFileChange) != 0;
        if (triggeredByFileChange)
        {
            NoteFileChangeBuildStarted();
        }

        var buildReason = triggeredByFileChange
            ? pendingBuildReason switch
            {
                "file change (queued)" => "file change (queued)",
                _ => "file change"
            }
            : pendingBuildReason;
        pendingBuildReason = "startup";
        var fileChangePaths = triggeredByFileChange ? lastFileChangePaths : null;
        lastFileChangePaths = [];
        RecordBuildTrigger(
            BuildTriggerKindFormatter.FromBuildReason(buildReason, triggeredByFileChange),
            buildReason,
            detail: triggeredByFileChange ? lastFileChangeTriggerDetail : null,
            fileChangePaths);
        lastFileChangeTriggerDetail = null;

        buildCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        currentBuildReasonInFlight = buildReason;
        var buildToken = buildCancellationSource.Token;

        try
        {
            if (runProcess is not null)
            {
                SetProjectCurrentAction("Building — stopping app");
                await StopRunProcessAsync(buildToken);
                await Task.Delay(500, buildToken);
            }

            lock (liveOutputSync)
            {
                liveBuildOutput.Clear();
            }

            watchRebuildInProgress = false;
            Interlocked.Exchange(ref liveOutputRevision, 0);
            lastErrorPreview = null;

            var buildBanner = WriteBuildStartBanner(buildReason);
            SetState(ProjectLifecycleState.Building);
            SetProjectCurrentAction($"Building — {buildReason}");

            buildProgressTracker = new BuildProgressTracker();
            buildProgressTracker.Reset();
            progressSteps = buildProgressTracker.Steps;
            NotifyProgressChanged(force: true);

            var releaseLocks = Local.RunOptions.ReleaseOutputLocksBeforeBuild;
            if (releaseLocks)
            {
                SetProjectCurrentAction("Building — releasing output locks");
                await ReleaseOutputLocksAsync(buildToken);
            }

            SetProjectCurrentAction("Building — dotnet build");
            var forceFullRebuild = DotNetBuildArguments.ShouldForceFullRebuild(
                buildReason,
                Local.RunOptions.ForceCompleteWarningCounts);
            var args = BuildProjectArgs(forceFullRebuild);
            Interlocked.Exchange(ref compileInProgress, 1);
            var result = await RunBuildAttemptAsync(args, buildToken, buildBanner);

            if (result.WasCancelled)
            {
                await HandleCancelledBuildAsync(buildReason, result, buildBanner, cancellationToken);
                return;
            }

            if (releaseLocks
                && result.ExitCode != 0
                && BuildLogParser.IsOutputLockError(result.Output))
            {
                await ReleaseOutputLocksAsync(cancellationToken);
                await Task.Delay(1000, cancellationToken);

                lock (liveOutputSync)
                {
                    liveBuildOutput.Clear();
                }

                Interlocked.Exchange(ref liveOutputRevision, 0);
                var retryBanner = WriteBuildStartBanner($"{buildReason} (lock retry)");
                buildProgressTracker = new BuildProgressTracker();
                buildProgressTracker.Reset();
                progressSteps = buildProgressTracker.Steps;
                NotifyProgressChanged(force: true);

                result = await RunBuildAttemptAsync(args, buildToken, retryBanner);
                if (result.WasCancelled)
                {
                    await HandleCancelledBuildAsync(buildReason, result, retryBanner, cancellationToken);
                    return;
                }
            }

            if (result.ExitCode != 0
                && Local.RunOptions.AutoRepairCorruptedOutput
                && CorruptedOutputTreeDetector.IsCorruptedTreeFailure(result.Output, Local.RootFolder))
            {
                SetProjectCurrentAction("Building — repairing output folders");
                var repair = await RepairBuildOutputInternalAsync(cancellationToken, restartAfter: false);
                if (repair.Repaired)
                {
                    notifyUser?.Invoke(
                        projectSettings.Id,
                        $"Repaired build output — {projectSettings.DisplayName}",
                        $"Removed {string.Join(", ", repair.RemovedFolders)}. Retrying build…",
                        UserNotificationKind.Warning,
                        UserNotificationCategory.Warning);

                    lock (liveOutputSync)
                    {
                        liveBuildOutput.Clear();
                    }

                    Interlocked.Exchange(ref liveOutputRevision, 0);
                    var repairBanner = WriteBuildStartBanner($"{buildReason} (output repair retry)");
                    buildProgressTracker = new BuildProgressTracker();
                    buildProgressTracker.Reset();
                    progressSteps = buildProgressTracker.Steps;
                    NotifyProgressChanged(force: true);
                    result = await RunBuildAttemptAsync(args, buildToken, repairBanner);
                    if (result.WasCancelled)
                    {
                        await HandleCancelledBuildAsync(buildReason, result, repairBanner, cancellationToken);
                        return;
                    }
                }
            }

            lastBuildExitCode = result.ExitCode;
            lastExitCode = result.ExitCode;
            lastDuration = result.Duration;

            // Always use this build's MSBuild summary — never Math.Max with prior counts or
            // ParseIssues line caps (maxWarnings=2000), which previously stuck the tray at 2000.
            var parsedErrors = BuildLogParser.ParseErrorCount(result.Output);
            var parsedWarnings = BuildLogParser.ParseWarningCount(result.Output);
            if (result.ExitCode == 0)
            {
                buildErrorCount = 0;
                buildWarningCount = parsedWarnings;
            }
            else
            {
                buildErrorCount = parsedErrors;
                buildWarningCount = parsedWarnings;
                if (buildErrorCount == 0)
                {
                    var (_, errorLines) = BuildLogParser.ParseErrors(result.Output);
                    if (errorLines.Count > 0)
                    {
                        buildErrorCount = errorLines.Count;
                    }
                }
            }

            var finishBanner = BuildMonitorLogBanner.FormatFinished(buildNumber, result.ExitCode);
            var logText = result.Output + Environment.NewLine + finishBanner;

            var buildLog = await logStore.SaveAsync(
                projectSettings.Id,
                BuildLogKind.Build,
                result.CommandLine,
                result.ExitCode,
                DateTimeOffset.UtcNow - result.Duration,
                logText,
                cancellationToken);

            lastBuildFinishedAtUtc = buildLog.FinishedAtUtc;
            lastErrorPreview = buildLog.ErrorLines.FirstOrDefault();
            if (result.Duration.TotalMilliseconds > 0)
            {
                burstStatsStore.RecordBuildDuration(
                    projectSettings.Id,
                    (int)result.Duration.TotalMilliseconds,
                    result.ExitCode == 0);
            }

            // Compile finished — release user-action gates before post-build tests/restart work.
            Interlocked.Exchange(ref compileInProgress, 0);

            if (result.ExitCode == 0)
            {
                progressSteps = [];
                SetState(ProjectLifecycleState.BuildOk);
            if (Local.RunOptions.RunTests == TestRunTrigger.OnBuildSuccess
                && !ShouldSkipAutoBuildTests())
            {
                PrepareTest("build success");
                await TestAsync(cancellationToken);
            }
            }
            else
            {
                SetState(ProjectLifecycleState.BuildFailed);
            }

            if (buildProgressTracker is not null)
            {
                if (buildProgressTracker.FinalizeFromResult(result.ExitCode, result.Output))
                {
                    progressSteps = buildProgressTracker.Steps;
                    NotifyProgressChanged(force: true);
                }
            }

            buildProgressTracker = null;

            var restartedAfterBuild = false;
            if (Local.RunOptions.RestartAppAfterRebuild
                && Local.RunOptions.RunMode != ProjectRunMode.None
                && result.ExitCode == 0
                && runProcess?.IsRunning != true
                && Volatile.Read(ref shipCheckInProgress) == 0
                && Volatile.Read(ref agentRebuildInProgress) == 0
                && RunHostLifecyclePolicy.MayStartOrRestartHost(desiredRunHostState)
                && !watchPausedByControlPlane)
            {
                if (triggeredByFileChange)
                {
                    await Task.Delay(1500, buildToken);
                }

                StartRunProcess(skipEmbeddedBuild: true);
                restartedAfterBuild = true;
            }

            ApplyPendingHotReloadRestartAfterBuild(result.ExitCode, restartedAfterBuild);
        }
        finally
        {
            buildCancellationSource?.Dispose();
            buildCancellationSource = null;
            currentBuildReasonInFlight = null;
            currentBuildTriggerId = null;
            Interlocked.Exchange(ref compileInProgress, 0);
            Interlocked.Exchange(ref buildInProgress, 0);
            Interlocked.Exchange(ref buildTriggeredByFileChange, 0);

            if (triggeredByFileChange)
            {
                fileChangeBuildCooldownUntil = DateTimeOffset.UtcNow.AddMilliseconds(
                    GetSessionAdjustedFileChangeDebounceMs());
            }
            else
            {
                var quietUntil = DateTimeOffset.UtcNow.AddMilliseconds(Math.Min(fileChangeDebounceMs / 2, 2000));
                if (quietUntil > fileChangeBuildCooldownUntil)
                {
                    fileChangeBuildCooldownUntil = quietUntil;
                }
            }

            if (pendingFileChangeRebuild
                && !BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode))
            {
                var nextReason = pendingRebuildHoldReason == PendingRebuildHoldReason.StartupDeferred
                    ? "startup"
                    : "file change (queued)";
                _ = WaitForEditQuietThenBuildAsync(nextReason);
            }
        }
    }

    private async Task HandleCancelledBuildAsync(
        string buildReason,
        CliRunResult result,
        string? buildBanner,
        CancellationToken cancellationToken)
    {
        var cancelBanner = "[BuildMonitor] Build cancelled — superseded by newer source changes.";
        var logText = result.Output;
        if (!string.IsNullOrWhiteSpace(logText) && !logText.EndsWith('\n'))
        {
            logText += Environment.NewLine;
        }

        logText += cancelBanner;

        await logStore.SaveAsync(
            projectSettings.Id,
            BuildLogKind.Build,
            result.CommandLine,
            result.ExitCode,
            DateTimeOffset.UtcNow - result.Duration,
            logText,
            cancellationToken);

        progressSteps = [];
        buildProgressTracker = null;
        EnterWaitingForEditsState("Build cancelled — waiting for edits to settle");

        notifyUser?.Invoke(
            projectSettings.Id,
            $"Build cancelled — {projectSettings.DisplayName}",
            "Newer source changes detected. Rebuilding when edits settle.",
            UserNotificationKind.Info,
            UserNotificationCategory.FileChangeDetected);
    }

    private async Task WaitForEditQuietThenBuildAsync(string buildReason)
    {
        var generation = Interlocked.Increment(ref fileChangeRebuildScheduleGeneration);

        // AI Controlled: never enter WaitingForEdits / quiet countdown for file-change schedules.
        if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode)
            && !string.Equals(buildReason, "startup", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnterWaitingForEditsState("Waiting for edits to settle…");

        while (generation == Volatile.Read(ref fileChangeRebuildScheduleGeneration))
        {
            var waitUntil = GetEffectiveEditQuietUntilUtc();

            if (fileChangeBuildCooldownUntil > waitUntil)
            {
                waitUntil = fileChangeBuildCooldownUntil;
            }

            var delay = waitUntil - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
                continue;
            }

            if (Volatile.Read(ref buildInProgress) != 0)
            {
                QueuePendingRebuild(
                    PendingRebuildHoldReason.BuildInProgress,
                    lastFileChangePaths,
                    wasAlreadyPending: true,
                    pathsAlreadyRelative: true);
                return;
            }

            // Mode may have flipped to AI Controlled mid-wait.
            if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode)
                && !string.Equals(buildReason, "startup", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsControlPlaneBusyBlockingAutoBuild())
            {
                await Task.Delay(500);
                continue;
            }

            if (EvaluateEditActivity().IsActive || DateTimeOffset.UtcNow < GetEffectiveEditQuietUntilUtc())
            {
                continue;
            }

            break;
        }

        if (generation != Volatile.Read(ref fileChangeRebuildScheduleGeneration))
        {
            return;
        }

        if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode)
            && !string.Equals(buildReason, "startup", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            QueuePendingRebuild(
                PendingRebuildHoldReason.BuildInProgress,
                lastFileChangePaths,
                wasAlreadyPending: true,
                pathsAlreadyRelative: true);
            return;
        }

        lastFileChangeTriggerDetail = BuildTriggerDetailFormatter.FormatCoalescedBuild(
            GetSessionAdjustedFileChangeDebounceMs(),
            pendingRebuildHoldReason,
            pendingRebuildTimerResetCount);
        ClearPendingRebuildHold();
        pendingFileChangeRebuild = false;

        if (string.Equals(buildReason, "startup", StringComparison.OrdinalIgnoreCase))
        {
            pendingBuildReason = "startup";
            await BuildAsync(CancellationToken.None);
            return;
        }

        Interlocked.Exchange(ref buildTriggeredByFileChange, 1);
        pendingBuildReason = "file change (queued)";

        notifyUser?.Invoke(
            projectSettings.Id,
            $"File change — {projectSettings.DisplayName}",
            "Source change detected. Rebuilding…",
            UserNotificationKind.Info,
            UserNotificationCategory.FileChangeDetected);

        await BuildAsync(CancellationToken.None);
    }

    private async Task HydrateLastBuildFromStoreAsync(CancellationToken cancellationToken)
    {
        var metadata = await logStore.LoadMetadataAsync(projectSettings.Id, BuildLogKind.Build, cancellationToken);
        if (metadata is null)
        {
            return;
        }

        lastBuildExitCode = metadata.ExitCode;
        lastExitCode = metadata.ExitCode;
        lastDuration = metadata.FinishedAtUtc - metadata.StartedAtUtc;
        lastBuildFinishedAtUtc = metadata.FinishedAtUtc;
        buildErrorCount = metadata.ExitCode == 0 ? 0 : metadata.ErrorCount;
        buildWarningCount = metadata.WarningCount;
        lastErrorPreview = metadata.ExitCode == 0 ? null : metadata.ErrorLines.FirstOrDefault();
        // Prefer counts re-parsed from the saved log so tray matches what the log viewer shows.
        var logText = await logStore.LoadLogTextAsync(metadata, maxBytes: 512_000, cancellationToken);
        if (!string.IsNullOrWhiteSpace(logText))
        {
            var (resolvedErrors, resolvedWarnings) = BuildIssueCountResolver.Resolve(logText);
            buildWarningCount = resolvedWarnings;
            buildErrorCount = metadata.ExitCode == 0 ? 0 : resolvedErrors;
        }

        RefreshHealth();
        HealthCoalesceRequested?.Invoke(true);
    }

    private string WriteBuildStartBanner(string reason)
    {
        var banner = BuildMonitorLogBanner.Format(Interlocked.Increment(ref buildNumber), reason);
        lock (liveOutputSync)
        {
            liveBuildOutput.AppendLine(banner);
            liveBuildOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveOutputRevision);
        return banner;
    }

    private async Task<CliRunResult> RunBuildAttemptAsync(
        List<string> args,
        CancellationToken cancellationToken,
        string? logBanner = null) =>
        await cliRunner.RunAsync(
            Local.RootFolder,
            args,
            cancellationToken,
            OnBuildOutputLine,
            logBanner);

    private async Task ReleaseOutputLocksAsync(CancellationToken cancellationToken)
    {
        var releaseResult = await OutputLockReleaser.ReleaseAsync(
            Local.RootFolder,
            Local.ProjectFile,
            cancellationToken);

        if (notifyUser is null)
        {
            return;
        }

        if (releaseResult.Failures.Count > 0)
        {
            var lines = new List<string>();
            if (releaseResult.ProcessesStopped > 0)
            {
                lines.Add($"Stopped {releaseResult.ProcessesStopped} process(es).");
            }

            lines.AddRange(releaseResult.Failures.Take(4));

            var accessDeniedOnly = releaseResult.Failures.All(OutputLockReleaser.IsAccessDeniedFailure);
            if (accessDeniedOnly)
            {
                lines.Add(string.Empty);
                lines.Add("Build Monitor cannot stop some processes without permission.");
                lines.Add("Close the running app yourself, or turn off \"Stop processes locking build output\" in Settings.");
            }

            notifyUser(
                projectSettings.Id,
                accessDeniedOnly
                    ? $"Couldn't release locks — {projectSettings.DisplayName}"
                    : $"Lock release issues — {projectSettings.DisplayName}",
                string.Join(Environment.NewLine, lines),
                accessDeniedOnly ? UserNotificationKind.Warning : UserNotificationKind.Error,
                accessDeniedOnly ? UserNotificationCategory.Warning : UserNotificationCategory.Error);
            return;
        }

        if (releaseResult.ProcessesStopped > 0)
        {
            notifyUser(
                projectSettings.Id,
                $"Released locks — {projectSettings.DisplayName}",
                string.Join(Environment.NewLine, releaseResult.StoppedDescriptions.Take(4)),
                UserNotificationKind.Info,
                UserNotificationCategory.Info);
        }
    }

    private void OnFileWatcherChanged(IReadOnlyList<string> changedPaths, int burstDurationMs)
    {
        if (burstDurationMs > 0)
        {
            burstStatsStore.RecordBurst(projectSettings.Id, burstDurationMs);
        }

        var meaningful = WatchIgnoreRules.FilterMeaningfulPaths(
            changedPaths,
            GetEffectiveWatchIgnoreSegments());
        if (meaningful.Count == 0)
        {
            return;
        }

        lastMeaningfulFileChangeUtc = DateTimeOffset.UtcNow;
        HeartbeatProjectWorker("file-watcher", $"{meaningful.Count} file(s)");
        if (Local.BuildControlMode == ProjectBuildControlMode.AiControlled)
        {
            SetProjectCurrentAction(
                $"AI Controlled — {meaningful.Count} change(s) detected (awaiting explicit build)");
        }
        else
        {
            SetProjectCurrentAction($"File change — rebuild pending ({meaningful.Count} file(s))");
        }

        RequestHealthCoalesce(immediate: true);

        lastFileChangePaths = RelativizePaths(meaningful);
        SyncFileWatcherDebounceMs();
        var wasAlreadyPending = pendingFileChangeRebuild;

        // Always observe; only schedule auto-build when policy allows.
        if (!ShouldScheduleAutoBuildFromFileChange())
        {
            if (IsControlPlaneBusyBlockingAutoBuild()
                && Local.BuildControlMode == ProjectBuildControlMode.FileWatching)
            {
                NoteAutoBuildBlockedByControlPlane();
                sessionStore?.TouchBusy(projectSettings.Id);
            }

            QueuePendingRebuild(PendingRebuildHoldReason.EditsSettling, meaningful, wasAlreadyPending);
            NotifyControlPlaneChanged(immediate: true);
            if (Local.BuildControlMode == ProjectBuildControlMode.FileWatching)
            {
                // Busy hold: keep waiting until idle, then debounce may resume.
                _ = WaitForEditQuietThenBuildAsync("file change (queued)");
            }
            else
            {
                // AI Controlled: observe only — no WaitingForEdits countdown / scheduler.
                SetProjectCurrentAction("AI Controlled — changes awaiting explicit build");
                if (state == ProjectLifecycleState.WaitingForEdits
                    && Volatile.Read(ref buildInProgress) == 0)
                {
                    SetState(runProcess?.IsRunning == true
                        ? (UsesDotNetWatchProcess() ? ProjectLifecycleState.Watching : ProjectLifecycleState.Running)
                        : ProjectLifecycleState.Idle);
                }

                RequestHealthCoalesce(immediate: true);
            }

            return;
        }

        if (DateTimeOffset.UtcNow < fileChangeBuildCooldownUntil)
        {
            QueuePendingRebuild(PendingRebuildHoldReason.PostBuildCooldown, meaningful, wasAlreadyPending);
            SchedulePendingRebuildWhenReady("file change (queued)");
            return;
        }

        if (Volatile.Read(ref testInProgress) != 0)
        {
            QueuePendingRebuild(PendingRebuildHoldReason.TestsInProgress, meaningful, wasAlreadyPending);
            SchedulePendingRebuildWhenReady("file change (queued)");
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            QueuePendingRebuild(PendingRebuildHoldReason.BuildInProgress, meaningful, wasAlreadyPending);
            if (BuildSuppressionPolicy.ShouldCancelInFlightBuild(
                    GetSuppressionSettings(),
                    currentBuildReasonInFlight))
            {
                pendingRebuildHoldReason = PendingRebuildHoldReason.SupersededByNewEdits;
                RequestBuildCancellation();
                Interlocked.Increment(ref fileChangeRebuildScheduleGeneration);
            }

            return;
        }

        if (IsAgentEditSessionActive() || EvaluateEditActivity().IsActive)
        {
            var reason = wasAlreadyPending
                ? PendingRebuildHoldReason.EditsStillArriving
                : PendingRebuildHoldReason.EditsSettling;
            QueuePendingRebuild(reason, meaningful, wasAlreadyPending);
            _ = WaitForEditQuietThenBuildAsync("file change (queued)");
            return;
        }

        Interlocked.Exchange(ref buildTriggeredByFileChange, 1);
        pendingBuildReason = "file change";
        lastFileChangeTriggerDetail = BuildTriggerDetailFormatter.FormatImmediateDebounce(
            GetSessionAdjustedFileChangeDebounceMs());

        notifyUser?.Invoke(
            projectSettings.Id,
            $"File change — {projectSettings.DisplayName}",
            "Source change detected. Rebuilding…",
            UserNotificationKind.Info,
            UserNotificationCategory.FileChangeDetected);

        _ = BuildAsync(CancellationToken.None);
    }

    private bool ShouldScheduleAutoBuildFromFileChange()
    {
        var session = sessionStore?.GetStatus(projectSettings.Id);
        return BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
            Local.BuildControlMode,
            session?.SessionApiUsed == true,
            session?.State ?? ControlPlaneSessionState.Idle);
    }

    private void SchedulePendingRebuildWhenReady(string buildReason)
    {
        if (!pendingFileChangeRebuild)
        {
            return;
        }

        if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode))
        {
            return;
        }

        _ = WaitForEditQuietThenBuildAsync(buildReason);
    }

    private void QueuePendingRebuild(
        PendingRebuildHoldReason reason,
        IReadOnlyList<string> meaningfulPaths,
        bool wasAlreadyPending,
        bool pathsAlreadyRelative = false)
    {
        pendingFileChangeRebuild = true;

        pendingRebuildHoldReason = reason == PendingRebuildHoldReason.EditsSettling && wasAlreadyPending
            ? PendingRebuildHoldReason.EditsStillArriving
            : reason;

        if (pendingRebuildHoldReason == PendingRebuildHoldReason.EditsStillArriving)
        {
            pendingRebuildTimerResetCount++;
        }

        pendingRebuildHoldFileCount = meaningfulPaths.Count;
        pendingRebuildHoldSamplePaths = pathsAlreadyRelative
            ? meaningfulPaths.Take(3).ToList()
            : RelativizePaths(meaningfulPaths).Take(3).ToList();
    }

    private void ClearPendingRebuildHold()
    {
        pendingRebuildHoldReason = PendingRebuildHoldReason.None;
        pendingRebuildHoldFileCount = 0;
        pendingRebuildHoldSamplePaths = [];
        pendingRebuildTimerResetCount = 0;
    }

}
