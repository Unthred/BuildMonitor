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
                definition.RootFolder,
                fileChangeDebounceMs,
                WatchExcludeSegments.Parse(definition.RunOptions.WatchExcludeSegments));
            fileWatcher.Changed += OnFileWatcherChanged;
        }
        catch (Exception ex)
        {
            notifyUser?.Invoke(
                definition.Id,
                $"File watcher disabled — {definition.DisplayName}",
                $"Could not watch '{definition.RootFolder}': {ex.Message}",
                UserNotificationKind.Warning,
                UserNotificationCategory.Warning);
        }
    }

    public async Task BuildAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref buildInProgress, 1, 0) != 0)
        {
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
            detail: null,
            fileChangePaths);

        fileWatcher?.Suspend();

        try
        {
            if (runProcess is not null)
            {
                SetProjectCurrentAction("Building — stopping app");
                await StopRunProcessAsync(cancellationToken);
                await Task.Delay(500, cancellationToken);
            }

            lock (liveOutputSync)
            {
                liveBuildOutput.Clear();
            }

            watchRebuildInProgress = false;
            Interlocked.Exchange(ref liveOutputRevision, 0);
            buildErrorCount = 0;
            buildWarningCount = 0;
            lastErrorPreview = null;

            var buildBanner = WriteBuildStartBanner(buildReason);
            SetState(ProjectLifecycleState.Building);
            SetProjectCurrentAction($"Building — {buildReason}");

            buildProgressTracker = new BuildProgressTracker();
            buildProgressTracker.Reset();
            progressSteps = buildProgressTracker.Steps;
            NotifyProgressChanged(force: true);

            var releaseLocks = definition.RunOptions.ReleaseOutputLocksBeforeBuild;
            if (releaseLocks)
            {
                SetProjectCurrentAction("Building — releasing output locks");
                await ReleaseOutputLocksAsync(cancellationToken);
            }

            SetProjectCurrentAction("Building — dotnet build");
            var args = BuildProjectArgs();
            var result = await RunBuildAttemptAsync(args, cancellationToken, buildBanner);

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

                result = await RunBuildAttemptAsync(args, cancellationToken, retryBanner);
            }

            if (result.ExitCode != 0
                && definition.RunOptions.AutoRepairCorruptedOutput
                && CorruptedOutputTreeDetector.IsCorruptedTreeFailure(result.Output, definition.RootFolder))
            {
                SetProjectCurrentAction("Building — repairing output folders");
                var repair = await RepairBuildOutputInternalAsync(cancellationToken, restartAfter: false);
                if (repair.Repaired)
                {
                    notifyUser?.Invoke(
                        definition.Id,
                        $"Repaired build output — {definition.DisplayName}",
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
                    result = await RunBuildAttemptAsync(args, cancellationToken, repairBanner);
                }
            }

            lastBuildExitCode = result.ExitCode;
            lastExitCode = result.ExitCode;
            lastDuration = result.Duration;

            var finishBanner = BuildMonitorLogBanner.FormatFinished(buildNumber, result.ExitCode);
            var logText = result.Output + Environment.NewLine + finishBanner;

            var buildLog = await logStore.SaveAsync(
                definition.Id,
                BuildLogKind.Build,
                result.CommandLine,
                result.ExitCode,
                DateTimeOffset.UtcNow - result.Duration,
                logText,
                cancellationToken);

            lastBuildFinishedAtUtc = buildLog.FinishedAtUtc;
            buildErrorCount = buildLog.ErrorCount;
            buildWarningCount = BuildLogParser.ParseWarningCount(result.Output);
            lastErrorPreview = buildLog.ErrorLines.FirstOrDefault();
            if (result.Duration.TotalMilliseconds > 0)
            {
                burstStatsStore.RecordBuildDuration(definition.Id, (int)result.Duration.TotalMilliseconds);
            }

            if (result.ExitCode == 0)
            {
                SetState(ProjectLifecycleState.BuildOk);
                if (definition.RunOptions.RunTests == TestRunTrigger.OnBuildSuccess)
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
            if (definition.RunOptions.RestartAppAfterRebuild
                && definition.RunOptions.RunMode != ProjectRunMode.None
                && result.ExitCode == 0
                && runProcess?.IsRunning != true)
            {
                if (triggeredByFileChange)
                {
                    await Task.Delay(1500, cancellationToken);
                }

                StartRunProcess(skipEmbeddedBuild: true);
                restartedAfterBuild = true;
            }

            ApplyPendingHotReloadRestartAfterBuild(result.ExitCode, restartedAfterBuild);
        }
        finally
        {
            Interlocked.Exchange(ref buildInProgress, 0);
            Interlocked.Exchange(ref buildTriggeredByFileChange, 0);
            fileWatcher?.Resume();

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

            if (pendingFileChangeRebuild && lastFileChangePaths.Count > 0)
            {
                pendingFileChangeRebuild = false;
                _ = ScheduleCoalescedFileChangeRebuildAsync();
            }
        }
    }

    private async Task ScheduleCoalescedFileChangeRebuildAsync()
    {
        var generation = Interlocked.Increment(ref fileChangeRebuildScheduleGeneration);

        while (generation == Volatile.Read(ref fileChangeRebuildScheduleGeneration))
        {
            var waitUntil = GetFileChangeQuietUntilUtc();
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
                pendingFileChangeRebuild = true;
                return;
            }

            if (DateTimeOffset.UtcNow < GetFileChangeQuietUntilUtc())
            {
                continue;
            }

            break;
        }

        if (generation != Volatile.Read(ref fileChangeRebuildScheduleGeneration))
        {
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            pendingFileChangeRebuild = true;
            return;
        }

        pendingFileChangeRebuild = false;
        Interlocked.Exchange(ref buildTriggeredByFileChange, 1);
        pendingBuildReason = "file change (queued)";

        notifyUser?.Invoke(
            definition.Id,
            $"File change — {definition.DisplayName}",
            "Source change detected. Rebuilding…",
            UserNotificationKind.Info,
            UserNotificationCategory.FileChangeDetected);

        await BuildAsync(CancellationToken.None);
    }

    private async Task HydrateLastBuildFromStoreAsync(CancellationToken cancellationToken)
    {
        var metadata = await logStore.LoadMetadataAsync(definition.Id, BuildLogKind.Build, cancellationToken);
        if (metadata is null)
        {
            return;
        }

        lastBuildExitCode = metadata.ExitCode;
        lastExitCode = metadata.ExitCode;
        lastDuration = metadata.FinishedAtUtc - metadata.StartedAtUtc;
        lastBuildFinishedAtUtc = metadata.FinishedAtUtc;
        buildErrorCount = metadata.ErrorCount;
        lastErrorPreview = metadata.ErrorLines.FirstOrDefault();
        var logText = await logStore.LoadLogTextAsync(metadata, maxBytes: 512_000, cancellationToken);
        if (!string.IsNullOrWhiteSpace(logText))
        {
            buildWarningCount = BuildLogParser.ParseWarningCount(logText);
            if (buildErrorCount == 0)
            {
                buildErrorCount = BuildLogParser.ParseErrorCount(logText);
            }
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
            definition.RootFolder,
            args,
            cancellationToken,
            OnBuildOutputLine,
            logBanner);

    private async Task ReleaseOutputLocksAsync(CancellationToken cancellationToken)
    {
        var releaseResult = await OutputLockReleaser.ReleaseAsync(
            definition.RootFolder,
            definition.ProjectFile,
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
                definition.Id,
                accessDeniedOnly
                    ? $"Couldn't release locks — {definition.DisplayName}"
                    : $"Lock release issues — {definition.DisplayName}",
                string.Join(Environment.NewLine, lines),
                accessDeniedOnly ? UserNotificationKind.Warning : UserNotificationKind.Error,
                accessDeniedOnly ? UserNotificationCategory.Warning : UserNotificationCategory.Error);
            return;
        }

        if (releaseResult.ProcessesStopped > 0)
        {
            notifyUser(
                definition.Id,
                $"Released locks — {definition.DisplayName}",
                string.Join(Environment.NewLine, releaseResult.StoppedDescriptions.Take(4)),
                UserNotificationKind.Info,
                UserNotificationCategory.Info);
        }
    }

    private void OnFileWatcherChanged(IReadOnlyList<string> changedPaths, int burstDurationMs)
    {
        if (burstDurationMs > 0)
        {
            burstStatsStore.RecordBurst(definition.Id, burstDurationMs);
        }

        var meaningful = WatchIgnoreRules.FilterMeaningfulPaths(
            changedPaths,
            WatchExcludeSegments.Parse(definition.RunOptions.WatchExcludeSegments));
        if (meaningful.Count == 0)
        {
            return;
        }

        lastMeaningfulFileChangeUtc = DateTimeOffset.UtcNow;
        HeartbeatProjectWorker("file-watcher", $"{meaningful.Count} file(s)");
        SetProjectCurrentAction($"File change — rebuild pending ({meaningful.Count} file(s))");

        lastFileChangePaths = RelativizePaths(meaningful);
        SyncFileWatcherDebounceMs();

        if (DateTimeOffset.UtcNow < fileChangeBuildCooldownUntil)
        {
            pendingFileChangeRebuild = true;
            return;
        }

        if (Volatile.Read(ref testInProgress) != 0)
        {
            pendingFileChangeRebuild = true;
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            pendingFileChangeRebuild = true;
            return;
        }

        if (IsAgentEditSessionActive())
        {
            pendingFileChangeRebuild = true;
            _ = ScheduleCoalescedFileChangeRebuildAsync();
            return;
        }

        Interlocked.Exchange(ref buildTriggeredByFileChange, 1);
        pendingBuildReason = "file change";

        notifyUser?.Invoke(
            definition.Id,
            $"File change — {definition.DisplayName}",
            "Source change detected. Rebuilding…",
            UserNotificationKind.Info,
            UserNotificationCategory.FileChangeDetected);

        _ = BuildAsync(CancellationToken.None);
    }

}
