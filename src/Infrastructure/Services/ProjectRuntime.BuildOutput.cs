using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed partial class ProjectRuntime
{
    private void OnBuildOutputLine(string line)
    {
        lock (liveOutputSync)
        {
            liveBuildOutput.AppendLine(line);
        }

        Interlocked.Increment(ref liveOutputRevision);
        HeartbeatProjectWorker("build-output");

        if (buildProgressTracker is not null && buildProgressTracker.OnOutputLine(line))
        {
            progressSteps = buildProgressTracker.Steps;
            // Regression guardrail:
            // Even when progress-tracking consumes build output (instead of the general "live output dirty"
            // path), we still need the health coalescer to publish an updated snapshot.
            MarkHealthDirty();
            RequestHealthCoalesce(immediate: false);
        }
        else
        {
            MarkHealthDirty();
            HealthCoalesceRequested?.Invoke(false);
        }

        TryHandleHotReloadRestartRequest(line);
    }

    private void TryHandleHotReloadRestartRequest(string line)
    {
        var request = HotReloadRestartDetector.Classify(line);
        if (request == HotReloadRestartRequest.None)
        {
            return;
        }

        if (BuildTriggerPolicy.IsAutoBuildDisabledByMode(definition.BuildControlMode)
            && Volatile.Read(ref agentRebuildInProgress) == 0
            && Volatile.Read(ref shipCheckInProgress) == 0)
        {
            return;
        }

        if (!definition.RunOptions.AutoRestartOnHotReloadRequest
            || definition.RunOptions.RunMode == ProjectRunMode.None)
        {
            return;
        }

        if (ShouldDeferRestartToDotNetWatch(line, request))
        {
            return;
        }

        ScheduleHotReloadRestart(request);
    }

    private bool ShouldDeferRestartToDotNetWatch(string line, HotReloadRestartRequest request) =>
        request == HotReloadRestartRequest.RestartApp
        && UsesDotNetWatchProcess()
        && definition.RunOptions.AutoRestartOnWatchChanges
        && HotReloadRestartDetector.IsWatchAutoRestartMessage(line);

    private void ScheduleHotReloadRestart(HotReloadRestartRequest request)
    {
        if (isRestarting || Volatile.Read(ref testInProgress) != 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - lastHotReloadRestartRequestUtc < TimeSpan.FromSeconds(5))
        {
            UpgradePendingHotReloadRestartRequest(request);
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            UpgradePendingHotReloadRestartRequest(request);
            return;
        }

        if (runProcess?.IsRunning != true)
        {
            if (request == HotReloadRestartRequest.RebuildAndRestart)
            {
                lastHotReloadRestartRequestUtc = now;
                _ = ExecuteHotReloadRestartAsync(HotReloadRestartRequest.RebuildAndRestart);
            }

            return;
        }

        lastHotReloadRestartRequestUtc = now;
        _ = ExecuteHotReloadRestartAsync(request);
    }

    private void UpgradePendingHotReloadRestartRequest(HotReloadRestartRequest request)
    {
        if (request == HotReloadRestartRequest.RebuildAndRestart)
        {
            Volatile.Write(ref pendingHotReloadRestartRequest, (int)HotReloadRestartRequest.RebuildAndRestart);
        }
        else if (Volatile.Read(ref pendingHotReloadRestartRequest) == 0)
        {
            Volatile.Write(ref pendingHotReloadRestartRequest, (int)request);
        }
    }

    private void ApplyPendingHotReloadRestartAfterBuild(int exitCode, bool restartedAfterBuild)
    {
        var pending = (HotReloadRestartRequest)Interlocked.Exchange(ref pendingHotReloadRestartRequest, 0);
        if (pending == HotReloadRestartRequest.None || exitCode != 0)
        {
            return;
        }

        if (restartedAfterBuild)
        {
            return;
        }

        if (definition.RunOptions.RunMode == ProjectRunMode.None)
        {
            return;
        }

        StartRunProcess(skipEmbeddedBuild: true);
    }

    private async Task ExecuteHotReloadRestartAsync(HotReloadRestartRequest request)
    {
        if (isRestarting
            || Volatile.Read(ref buildInProgress) != 0
            || Volatile.Read(ref testInProgress) != 0)
        {
            UpgradePendingHotReloadRestartRequest(request);
            return;
        }

        notifyUser?.Invoke(
            definition.Id,
            request == HotReloadRestartRequest.RebuildAndRestart
                ? $"Rebuild required — {definition.DisplayName}"
                : $"Restart required — {definition.DisplayName}",
            "Output indicated hot reload could not apply the latest changes. Restarting automatically.",
            UserNotificationKind.Info,
            UserNotificationCategory.Info);

        try
        {
            await RestartAppCoreAsync(
                rebuildFirst: request == HotReloadRestartRequest.RebuildAndRestart,
                CancellationToken.None,
                request == HotReloadRestartRequest.RebuildAndRestart
                    ? "hot reload rebuild"
                    : "hot reload restart");
        }
        catch (Exception ex)
        {
            notifyUser?.Invoke(
                definition.Id,
                $"Auto-restart failed — {definition.DisplayName}",
                ex.Message,
                UserNotificationKind.Warning,
                UserNotificationCategory.Warning);
        }
    }

    private bool RefreshBuildIssueCountsFromWatchOutput(bool force)
    {
        if (!UsesDotNetWatchProcess() || runProcess is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && (now - lastLiveCountParseUtc).TotalMilliseconds < 150)
        {
            return false;
        }

        lastLiveCountParseUtc = now;
        var output = BuildLogTextNormalizer.Normalize(runProcess.Output);
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var buildSegment = BuildLogParser.ExtractLatestBuildResultSegment(output);
        if (string.IsNullOrWhiteSpace(buildSegment))
        {
            return false;
        }

        var logPath = logStore.GetLogPath(definition.Id, BuildLogKind.Build);
        var (parsedErrors, parsedWarnings) = BuildIssueCountResolver.Resolve(buildSegment, logPath);
        if (!BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
                buildSegment,
                buildErrorCount,
                buildWarningCount,
                parsedErrors,
                parsedWarnings))
        {
            return false;
        }

        buildErrorCount = parsedErrors;
        buildWarningCount = parsedWarnings;
        return true;
    }

    private bool RefreshLiveIssueCounts(bool force)
    {
        if (state is not (ProjectLifecycleState.Building or ProjectLifecycleState.Testing))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && (now - lastLiveCountParseUtc).TotalMilliseconds < 150)
        {
            return false;
        }

        lastLiveCountParseUtc = now;
        string output;
        lock (liveOutputSync)
        {
            output = state == ProjectLifecycleState.Testing
                ? liveTestOutput.ToString()
                : liveBuildOutput.ToString();
        }

        var normalized = BuildLogTextNormalizer.Normalize(output);
        var (parsedErrors, parsedWarnings) = state == ProjectLifecycleState.Testing
            ? CountLiveIssues(BuildLogKind.Test, normalized)
            : CountLiveBuildIssues(normalized);
        if (parsedErrors == buildErrorCount && parsedWarnings == buildWarningCount)
        {
            return false;
        }

        // Mid-build: don't clear existing counts until MSBuild prints a definitive outcome.
        if (parsedWarnings == 0
            && parsedErrors == 0
            && (buildWarningCount > 0 || buildErrorCount > 0)
            && !BuildIssueCountResolver.HasDefinitiveBuildOutcome(normalized))
        {
            return false;
        }

        buildErrorCount = parsedErrors;
        buildWarningCount = parsedWarnings;
        return true;
    }

    private void NotifyProgressChanged(bool force = false) =>
        RequestHealthCoalesce(force);

    private List<string> BuildProjectArgs(bool forceFullRebuild = false)
    {
        var args = new List<string> { "build", ResolveProjectFileArg() };
        DotNetBuildArguments.ApplyFullRebuildFlag(args, forceFullRebuild);
        if (!string.IsNullOrWhiteSpace(shipCheckConfiguration))
        {
            args.Add("-c");
            args.Add(shipCheckConfiguration);
        }

        AppendExtraArgs(args);
        return args;
    }

    private string ResolveProjectFileArg() =>
        Path.IsPathRooted(definition.ProjectFile)
            ? definition.ProjectFile
            : Path.Combine(definition.RootFolder, definition.ProjectFile);
    public async Task<BuildOutputRepairResult> RepairBuildOutputAsync(
        CancellationToken cancellationToken,
        bool restartAfter)
    {
        fileWatcher?.Suspend();
        try
        {
            if (runProcess is not null)
            {
                await StopRunProcessAsync(cancellationToken);
                await Task.Delay(500, cancellationToken);
            }

            var result = BuildOutputTreeRepairer.Repair(definition.RootFolder);
            if (result.Repaired && restartAfter && definition.RunOptions.RunMode != ProjectRunMode.None)
            {
                StartRunProcess(skipEmbeddedBuild: false);
            }

            return result;
        }
        finally
        {
            fileWatcher?.Resume();
        }
    }

    private Task<BuildOutputRepairResult> RepairBuildOutputInternalAsync(
        CancellationToken cancellationToken,
        bool restartAfter) =>
        RepairBuildOutputAsync(cancellationToken, restartAfter);

    private bool baseOutputPathWarningShown;

    private void WarnIfRiskyBaseOutputPath()
    {
        if (baseOutputPathWarningShown
            || definition.RunOptions.RunMode != ProjectRunMode.Watch
            || !CorruptedOutputTreeDetector.HasRiskyBaseOutputPath(definition.ExtraDotNetArgs))
        {
            return;
        }

        baseOutputPathWarningShown = true;
        notifyUser?.Invoke(
            definition.Id,
            $"Risky build args — {definition.DisplayName}",
            "Extra dotnet args include BaseOutputPath while watch mode is enabled. "
            + "This can corrupt artifacts/bin/obj output trees. Remove BaseOutputPath for local watch.",
            UserNotificationKind.Warning,
            UserNotificationCategory.Warning);
    }
}
