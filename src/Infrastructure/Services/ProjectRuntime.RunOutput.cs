using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed partial class ProjectRuntime
{
    private void OnRunProcessOutputLine(string line)
    {
        Interlocked.Increment(ref liveOutputRevision);
        HeartbeatProjectWorker("run-output");

        if (DotNetRunOutputParser.TryExtractListeningUrl(line, out var parsedUrl))
        {
            var hadUrl = !string.IsNullOrWhiteSpace(pendingListenUrl);
            pendingListenUrl = parsedUrl;
            var wasReady = listenUrlReady;
            RefreshListenUrlReady();
            if (!hadUrl || listenUrlReady != wasReady)
            {
                NotifyProgressChanged(force: true);
            }
        }

        if (DotNetRunOutputParser.IsHostTerminatedLine(line)
            || DotNetRunOutputParser.IsFatalStartupLine(line))
        {
            lastErrorPreview = line.Trim();
            runErrorCount = Math.Max(runErrorCount, 1);
            SetState(ProjectLifecycleState.Crashed);
            notifyUser?.Invoke(
                definition.Id,
                $"App failed to start — {definition.DisplayName}",
                line.Trim(),
                UserNotificationKind.Error,
                UserNotificationCategory.Error);
            SaveRunOutputIfChanged(force: true);
            return;
        }

        TryHandleHotReloadRestartRequest(line);

        if (UsesDotNetWatchProcess())
        {
            HandleWatchProcessOutputLine(line);
        }

        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(false);
    }

    private void HandleWatchProcessOutputLine(string line)
    {
        if (DotNetWatchOutput.IsWatchBuildingLine(line))
        {
            RecordBuildTrigger(
                BuildTriggerKind.DotNetWatchCompile,
                "dotnet watch compile started",
                detail: line.Trim());
            watchRebuildInProgress = true;
            return;
        }

        if (DotNetWatchOutput.IsBuildFailedLine(line))
        {
            watchRebuildInProgress = false;
            lastBuildExitCode = 1;
            lastErrorPreview = line.Trim();
            buildErrorCount = Math.Max(buildErrorCount, 1);
            RefreshBuildIssueCountsFromWatchOutput(force: true);
            if (runProcess?.IsRunning == true)
            {
                RefreshHealth();
                NotifyProgressChanged(force: true);
                HealthCoalesceRequested?.Invoke(true);
            }
            else
            {
                SetState(ProjectLifecycleState.BuildFailed);
            }

            return;
        }

        if (DotNetWatchOutput.IsBuildSucceededLine(line))
        {
            var wasWatchRebuild = watchRebuildInProgress;
            watchRebuildInProgress = false;
            lastBuildExitCode = 0;
            RefreshBuildIssueCountsFromWatchOutput(force: true);
            if (state is ProjectLifecycleState.BuildFailed)
            {
                SetState(ProjectLifecycleState.Watching);
            }

            if (wasWatchRebuild)
            {
                notifyUser?.Invoke(
                    definition.Id,
                    $"Build succeeded — {definition.DisplayName}",
                    "Watch rebuild completed successfully.",
                    UserNotificationKind.Info,
                    UserNotificationCategory.BuildSuccess);
            }

            RequestHealthCoalesce(immediate: true);
            return;
        }

        if (!DotNetWatchOutput.IsFileChangeLine(line))
        {
            return;
        }

        if (watchRebuildInProgress
            || Volatile.Read(ref testInProgress) != 0
            || DateTimeOffset.UtcNow < fileChangeBuildCooldownUntil)
        {
            return;
        }

        watchRebuildInProgress = true;
        listenUrlReady = false;
        listenUrlNotified = false;
        RecordBuildTrigger(
            BuildTriggerKind.DotNetWatchFileChange,
            "dotnet watch detected a file change",
            detail: line.Trim());

        var now = DateTimeOffset.UtcNow;
        var notifyCooldown = TimeSpan.FromMilliseconds(Math.Max(fileChangeDebounceMs, 2000));
        if (now - lastWatchFileChangeNotifyUtc < notifyCooldown)
        {
            return;
        }

        lastWatchFileChangeNotifyUtc = now;
        notifyUser?.Invoke(
            definition.Id,
            $"File change — {definition.DisplayName}",
            "Source change detected. Rebuilding…",
            UserNotificationKind.Info,
            UserNotificationCategory.FileChangeDetected);
    }

    private void StartRunLogSaveTimer()
    {
        StopRunLogSaveTimer();
        runLogSaveTimer = new Timer(
            _ => SaveRunOutputIfChanged(),
            null,
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(8));
    }

    private void StopRunLogSaveTimer()
    {
        runLogSaveTimer?.Dispose();
        runLogSaveTimer = null;
    }

    private void SaveRunOutputIfChanged(bool force = false)
    {
        var process = runProcess;
        if (process is null)
        {
            return;
        }

        var revision = Volatile.Read(ref liveOutputRevision);
        if (!force && revision == runOutputSaveRevision)
        {
            return;
        }

        var output = BuildLogTextNormalizer.Normalize(process.Output);
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        runOutputSaveRevision = revision;
        var commandLine = process.CommandLine;
        var exitCode = state is ProjectLifecycleState.Crashed ? 1 : 0;

        _ = Task.Run(async () =>
        {
            try
            {
                await logStore.SaveAsync(
                    definition.Id,
                    BuildLogKind.Run,
                    commandLine,
                    exitCode,
                    DateTimeOffset.UtcNow,
                    output,
                    CancellationToken.None);
            }
            catch
            {
                // Best effort only — never block the hosted app on log I/O.
            }
        });
    }

    public void EnsureRunProcessStartedAfterBuild()
    {
        if (definition.RunOptions.RunMode == ProjectRunMode.None || lastBuildExitCode != 0)
        {
            return;
        }

        if (runProcess?.IsRunning == true)
        {
            return;
        }

        StartRunProcess(skipEmbeddedBuild: true);
    }

    public Task RestartAppAsync(CancellationToken cancellationToken) =>
        RestartAppCoreAsync(rebuildFirst: false, cancellationToken);

    public Task RebuildAndRestartAsync(CancellationToken cancellationToken) =>
        RestartAppCoreAsync(rebuildFirst: true, cancellationToken, "rebuild & restart");

    private async Task RestartAppCoreAsync(
        bool rebuildFirst,
        CancellationToken cancellationToken,
        string? buildReason = null)
    {
        if (definition.RunOptions.RunMode == ProjectRunMode.None)
        {
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            notifyUser?.Invoke(
                definition.Id,
                $"Restart skipped — {definition.DisplayName}",
                "Wait for the current build to finish, then try again.",
                UserNotificationKind.Warning,
                UserNotificationCategory.Warning);
            return;
        }

        isRestarting = true;
        HealthCoalesceRequested?.Invoke(true);

        try
        {
            await StopRunProcessAsync(cancellationToken);
            restartCount = 0;
            runErrorCount = 0;
            runWarningCount = 0;

            if (rebuildFirst)
            {
                PrepareBuild(buildReason ?? "rebuild & restart");
                await BuildAsync(cancellationToken);
            }
            else if (buildReason == "hot reload restart")
            {
                RecordBuildTrigger(
                    BuildTriggerKind.HotReloadRestart,
                    "Hot reload requested app restart (no rebuild)",
                    detail: null);
            }

            EnsureRunProcessStartedAfterBuild();
        }
        finally
        {
            isRestarting = false;
            HealthCoalesceRequested?.Invoke(true);
        }
    }
}
