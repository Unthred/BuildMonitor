using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed class ProjectRuntime : IDisposable
{
    private readonly BuildLogStore logStore;
    private readonly BuildTriggerJournal triggerJournal;
    private readonly FileChangeBurstStatsStore burstStatsStore;
    private readonly DotNetCliRunner cliRunner;
    private Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifyUser;
    private SupervisedProcess? runProcess;
    private DebouncedFileWatcher? fileWatcher;
    private LocalProjectDefinition definition;
    private ProjectLifecycleState state = ProjectLifecycleState.Idle;
    private MonitorHealth health = MonitorHealth.Unknown;
    private int restartCount;
    private string? lastErrorPreview;
    private int buildErrorCount;
    private int buildWarningCount;
    private int runErrorCount;
    private int runWarningCount;
    private bool isRestarting;
    private readonly object liveOutputSync = new();
    private readonly StringBuilder liveBuildOutput = new();
    private readonly StringBuilder liveTestOutput = new();
    private int liveOutputRevision;
    private int liveTestOutputRevision;
    private int testInProgress;
    private int testNumber;
    private string pendingTestReason = "tests";
    private bool watchRebuildInProgress;
    private int lastBuildExitCode = -1;
    private int? lastExitCode;
    private TimeSpan? lastDuration;
    private DateTimeOffset? lastBuildFinishedAtUtc;
    private DateTimeOffset lastChangedUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset lastLiveCountParseUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<BuildProgressStep> progressSteps = [];
    private BuildProgressTracker? buildProgressTracker;
    private int buildInProgress;
    private int buildTriggeredByFileChange;
    private bool pendingFileChangeRebuild;
    private DateTimeOffset fileChangeBuildCooldownUntil = DateTimeOffset.MinValue;
    private DateTimeOffset lastWatchFileChangeNotifyUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastHotReloadRestartRequestUtc = DateTimeOffset.MinValue;
    private int fileChangeDebounceMs = 3000;
    private int manualFileChangeDebounceMs = 3000;
    private FileChangeDebounceMode debounceMode = FileChangeDebounceMode.Manual;
    private bool coalesceWatchRebuilds = true;
    private int pendingHotReloadRestartRequest;
    private int buildNumber;
    private string pendingBuildReason = "startup";
    private DateTimeOffset lastMeaningfulFileChangeUtc = DateTimeOffset.MinValue;
    private int fileChangeRebuildScheduleGeneration;
    private readonly Queue<DateTimeOffset> recentFileChangeBuildStarts = new();
    private IReadOnlyList<string> lastFileChangePaths = [];
    private int runProcessGeneration;
    private Action<string, int>? runProcessExitedHandler;
    private string? pendingListenUrl;
    private IReadOnlyList<string> candidateListenUrls = [];
    private bool listenUrlReady;
    private bool listenUrlNotified;
    private int runOutputSaveRevision;
    private Timer? listenUrlPollTimer;
    private Timer? runLogSaveTimer;

    private int healthDirty;

    private readonly List<string> registeredWorkerIds = [];
    private readonly Dictionary<string, DateTimeOffset> lastWorkerHeartbeatUtc = new(StringComparer.OrdinalIgnoreCase);

    public event Action<bool>? HealthCoalesceRequested;

    public string ProjectId => definition.Id;
    public string DisplayName => definition.DisplayName;
    public bool IsRunProcessActive => runProcess?.IsRunning == true;
    public bool RestartAppAfterRebuild => definition.RunOptions.RestartAppAfterRebuild;

    public ProjectHealthSnapshot Snapshot => BuildSnapshot();

    public ProjectHealthSnapshot BuildSnapshot()
    {
        RefreshHealth();
        var (displayErrors, displayWarnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
                state,
                buildErrorCount,
                buildWarningCount,
                runErrorCount,
                runWarningCount);
            return new ProjectHealthSnapshot(
                definition.Id,
                definition.DisplayName,
                health,
                ProjectHealthEvaluator.ToLabel(health),
                state,
                lastExitCode,
                lastDuration,
                lastErrorPreview,
                displayErrors,
                displayWarnings,
                lastChangedUtc,
                lastBuildFinishedAtUtc,
                definition.IsActiveInSession,
                progressSteps,
                ResolveDisplayListenUrl(),
                listenUrlReady,
                definition.RunOptions.RunMode != ProjectRunMode.None,
                HealthIssueCountsFormatter.FormatStatusLine(
                    state,
                    buildErrorCount,
                    buildWarningCount,
                    runErrorCount,
                    runWarningCount),
                HealthIssueCountsFormatter.FormatFailurePhase(state),
                isRestarting);
    }

    public void MarkHealthDirty() => Interlocked.Exchange(ref healthDirty, 1);

    public bool TryCoalesceHealth()
    {
        if (Interlocked.Exchange(ref healthDirty, 0) == 0)
        {
            return false;
        }

        CoalesceHealthCore();
        return true;
    }

    public void ForceCoalesceHealth()
    {
        Interlocked.Exchange(ref healthDirty, 0);
        CoalesceHealthCore();
    }

    private void CoalesceHealthCore()
    {
        RefreshLiveIssueCounts(force: true);
        RefreshHealth();
    }

    private void RequestHealthCoalesce(bool immediate = false)
    {
        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(immediate);
    }

    public ProjectRuntime(
        LocalProjectDefinition definition,
        BuildLogStore logStore,
        DotNetCliRunner cliRunner,
        BuildTriggerJournal triggerJournal,
        FileChangeBurstStatsStore burstStatsStore,
        Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifyUser = null)
    {
        this.definition = definition;
        this.logStore = logStore;
        this.cliRunner = cliRunner;
        this.triggerJournal = triggerJournal;
        this.burstStatsStore = burstStatsStore;
        this.notifyUser = notifyUser;
        RegisterProjectWorkers();
    }

    public void UpdateDefinition(LocalProjectDefinition updated, GlobalMonitorSettings? monitor = null)
    {
        definition = updated;
        baseOutputPathWarningShown = false;
        if (monitor is null)
        {
            return;
        }

        if (monitor.FileChangeDebounceMs > 0)
        {
            manualFileChangeDebounceMs = monitor.FileChangeDebounceMs;
        }

        debounceMode = monitor.FileChangeDebounceMode;
        fileChangeDebounceMs = ResolveFileChangeDebounceMs();
        fileWatcher?.SetDebounceMs(fileChangeDebounceMs);
        coalesceWatchRebuilds = monitor.CoalesceWatchRebuilds;
    }

    private int ResolveFileChangeDebounceMs() =>
        AdaptiveFileChangeDebounce.ResolveEffectiveDebounce(
            debounceMode,
            manualFileChangeDebounceMs,
            burstStatsStore.GetOrDefault(definition.Id));

    private int GetSessionAdjustedFileChangeDebounceMs()
    {
        PruneRecentFileChangeBuildStarts();
        return AdaptiveFileChangeDebounce.ApplySessionPressure(
            ResolveFileChangeDebounceMs(),
            recentFileChangeBuildStarts.Count);
    }

    private void SyncFileWatcherDebounceMs()
    {
        var effective = GetSessionAdjustedFileChangeDebounceMs();
        if (effective != fileChangeDebounceMs)
        {
            fileChangeDebounceMs = effective;
            fileWatcher?.SetDebounceMs(effective);
        }
    }

    private DateTimeOffset GetFileChangeQuietUntilUtc() =>
        lastMeaningfulFileChangeUtc == DateTimeOffset.MinValue
            ? DateTimeOffset.UtcNow
            : AdaptiveFileChangeDebounce.ComputeQuietUntilUtc(
                lastMeaningfulFileChangeUtc,
                GetSessionAdjustedFileChangeDebounceMs());

    private void NoteFileChangeBuildStarted()
    {
        recentFileChangeBuildStarts.Enqueue(DateTimeOffset.UtcNow);
        PruneRecentFileChangeBuildStarts();
        SyncFileWatcherDebounceMs();
    }

    private void PruneRecentFileChangeBuildStarts()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-90);
        while (recentFileChangeBuildStarts.Count > 0
               && recentFileChangeBuildStarts.Peek() < cutoff)
        {
            recentFileChangeBuildStarts.Dequeue();
        }
    }

    private bool IsAgentEditSessionActive()
    {
        PruneRecentFileChangeBuildStarts();
        return recentFileChangeBuildStarts.Count >= 1;
    }

    public BuildIntelligenceSnapshot GetIntelligenceSnapshot(GlobalMonitorSettings monitor)
    {
        PruneRecentFileChangeBuildStarts();
        var stats = burstStatsStore.GetOrDefault(definition.Id);
        var liveDebounceMs = GetSessionAdjustedFileChangeDebounceMs();
        DateTimeOffset? rebuildQuietUntilUtc = pendingFileChangeRebuild
                                               && lastMeaningfulFileChangeUtc != DateTimeOffset.MinValue
            ? AdaptiveFileChangeDebounce.ComputeQuietUntilUtc(
                lastMeaningfulFileChangeUtc,
                liveDebounceMs)
            : null;

        return BuildIntelligenceSnapshot.Create(
            definition,
            monitor,
            stats,
            manualFileChangeDebounceMs,
            debounceMode,
            ResolveFileChangeDebounceMs(),
            liveDebounceMs,
            recentFileChangeBuildStarts.Count,
            coalesceWatchRebuilds,
            lastMeaningfulFileChangeUtc == DateTimeOffset.MinValue ? null : lastMeaningfulFileChangeUtc,
            pendingFileChangeRebuild,
            rebuildQuietUntilUtc);
    }

    private bool UsesCoalescedWatchRebuilds() =>
        definition.RunOptions.RunMode == ProjectRunMode.Watch && coalesceWatchRebuilds;

    private bool UsesDotNetWatchProcess() =>
        definition.RunOptions.RunMode == ProjectRunMode.Watch && !UsesCoalescedWatchRebuilds();

    private bool ShouldStartFileWatcher()
    {
        if (definition.RunOptions.FileChanges == FileChangeMode.Off)
        {
            return false;
        }

        if (UsesCoalescedWatchRebuilds())
        {
            return true;
        }

        return definition.RunOptions.FileChanges == FileChangeMode.TriggerRebuild
            && definition.RunOptions.RunMode != ProjectRunMode.Watch;
    }

    public void SetUserNotifier(Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifier) =>
        notifyUser = notifier;

    private static (int Errors, int Warnings) CountLiveIssues(BuildLogKind kind, string normalized) =>
        kind switch
        {
            BuildLogKind.Run => (
                DotNetRunOutputParser.ParseErrorCount(normalized),
                DotNetRunOutputParser.ParseWarningCount(normalized)),
            BuildLogKind.Test => CountTestIssues(normalized),
            _ => (
                BuildLogParser.ParseErrorCount(normalized),
                BuildLogParser.ParseWarningCount(normalized))
        };

    private static (int Errors, int Warnings) CountTestIssues(string normalized)
    {
        var testIssues = DotNetTestOutputParser.ParseIssues(normalized);
        return (testIssues.Count(i => i.IsError), testIssues.Count(i => !i.IsError));
    }

    public void PrepareBuild(string reason) => pendingBuildReason = reason;

    public void PrepareTest(string reason) => pendingTestReason = reason;

    public LiveBuildLogView? GetLiveBuildLogView(BuildLogKind kind)
    {
        var isDirectBuild = Volatile.Read(ref buildInProgress) != 0
            || state is ProjectLifecycleState.Building;
        var isWatchRebuild = watchRebuildInProgress && runProcess?.IsRunning == true;
        var isRunLive = kind == BuildLogKind.Run && runProcess?.IsRunning == true;
        var isTestLive = kind == BuildLogKind.Test
            && (Volatile.Read(ref testInProgress) != 0 || state is ProjectLifecycleState.Testing);

        if (kind == BuildLogKind.Run)
        {
            if (!isRunLive)
            {
                return null;
            }
        }
        else if (kind == BuildLogKind.Test)
        {
            if (!isTestLive)
            {
                return null;
            }
        }
        else if (kind != BuildLogKind.Build || (!isDirectBuild && !isWatchRebuild))
        {
            return null;
        }

        string text;
        lock (liveOutputSync)
        {
            text = kind switch
            {
                BuildLogKind.Test => liveTestOutput.ToString(),
                BuildLogKind.Build when isDirectBuild => liveBuildOutput.ToString(),
                _ => runProcess?.Output ?? string.Empty
            };
        }

        var normalized = BuildLogTextNormalizer.Normalize(text);
        var revision = kind == BuildLogKind.Test
            ? Volatile.Read(ref liveTestOutputRevision)
            : Volatile.Read(ref liveOutputRevision);
        var (liveErrors, liveWarnings) = CountLiveIssues(kind, normalized);
        return new LiveBuildLogView(
            normalized,
            true,
            state,
            liveErrors,
            liveWarnings,
            revision);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        SetProjectCurrentAction("Starting — loading saved build state");
        await HydrateLastBuildFromStoreAsync(cancellationToken);
        await BuildAsync(cancellationToken);

        if (definition.RunOptions.RunMode == ProjectRunMode.None)
        {
            return;
        }

        if (lastBuildExitCode != 0)
        {
            RefreshHealth();
            return;
        }

        // Build already completed above — skip watch/run's embedded rebuild.
        StartRunProcess(skipEmbeddedBuild: true);
        TryStartFileWatcher();
    }

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

        var parsedErrors = BuildLogParser.ParseErrorCount(output);
        var parsedWarnings = BuildLogParser.ParseWarningCount(output);
        if (parsedErrors == buildErrorCount && parsedWarnings == buildWarningCount)
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

        var parsedErrors = BuildLogParser.ParseErrorCount(output);
        var parsedWarnings = BuildLogParser.ParseWarningCount(output);
        if (parsedErrors == buildErrorCount && parsedWarnings == buildWarningCount)
        {
            return false;
        }

        buildErrorCount = parsedErrors;
        buildWarningCount = parsedWarnings;
        return true;
    }

    private void NotifyProgressChanged(bool force = false) =>
        RequestHealthCoalesce(force);

    public async Task TestAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref testInProgress, 1, 0) != 0)
        {
            notifyUser?.Invoke(
                definition.Id,
                $"Tests skipped — {definition.DisplayName}",
                "Tests are already running for this project.",
                UserNotificationKind.Warning,
                UserNotificationCategory.Warning);
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            Interlocked.Exchange(ref testInProgress, 0);
            notifyUser?.Invoke(
                definition.Id,
                $"Tests skipped — {definition.DisplayName}",
                "Wait for the current build to finish, then try again.",
                UserNotificationKind.Warning,
                UserNotificationCategory.Warning);
            return;
        }

        var testReason = pendingTestReason;
        pendingTestReason = "tests";
        var wasRunProcessActive = runProcess?.IsRunning == true;
        var releaseLocksSetting = definition.RunOptions.ReleaseOutputLocksBeforeBuild;
        var stoppedAppForTests = false;

        fileWatcher?.Suspend();
        fileChangeBuildCooldownUntil = DateTimeOffset.UtcNow.AddMinutes(2);

        try
        {
            lock (liveOutputSync)
            {
                liveTestOutput.Clear();
            }

            Interlocked.Exchange(ref liveTestOutputRevision, 0);
            buildErrorCount = 0;
            buildWarningCount = 0;
            lastErrorPreview = null;

            var resolution = TestProjectDiscovery.Resolve(
                definition.RootFolder,
                definition.ProjectFile,
                definition.TestProjectFile);

            if (resolution.Targets.Count == 0)
            {
                WriteTestStartBanner(testReason, [], resolution.DiscoveryNote);
                SetState(ProjectLifecycleState.TestFailed);
                lastErrorPreview = resolution.DiscoveryNote;
                buildErrorCount = 1;
                return;
            }

            WriteTestStartBanner(testReason, resolution);
            SetState(ProjectLifecycleState.Testing);
            NotifyProgressChanged(force: true);

            var startedAtUtc = DateTimeOffset.UtcNow;
            var commandLines = new List<string>();
            var exitCode = 0;
            var wallDuration = TimeSpan.Zero;

            for (var i = 0; i < resolution.Targets.Count; i++)
            {
                var target = resolution.Targets[i];
                if (resolution.Targets.Count > 1)
                {
                    AppendTestSectionHeader(i + 1, resolution.Targets.Count, target);
                }

                var targetRun = await RunTestTargetWithRetryAsync(
                    target,
                    wasRunProcessActive,
                    releaseLocksSetting,
                    cancellationToken);

                stoppedAppForTests |= targetRun.StoppedApp;
                commandLines.Add(targetRun.Result.CommandLine);
                wallDuration += targetRun.Result.Duration;
                if (targetRun.Result.ExitCode != 0)
                {
                    exitCode = targetRun.Result.ExitCode;
                }
            }

            string logText;
            lock (liveOutputSync)
            {
                logText = liveTestOutput.ToString();
            }

            var testsExecuted = DotNetTestOutputParser.LooksLikeTestsExecuted(logText);
            var testSummary = DotNetTestOutputParser.TryParseSummary(logText);
            var summaryLine = testSummary is not null
                ? DotNetTestOutputParser.FormatSummaryLine(testSummary)
                : DescribeMissingTestSummary(logText, testsExecuted);
            var finishBanner = BuildMonitorLogBanner.FormatTestFinished(
                testNumber,
                testsExecuted ? exitCode : 1,
                summaryLine,
                wallDuration);
            lock (liveOutputSync)
            {
                liveTestOutput.AppendLine(finishBanner);
            }

            Interlocked.Increment(ref liveTestOutputRevision);

            lock (liveOutputSync)
            {
                logText = liveTestOutput.ToString();
            }

            var parsed = BuildLogParser.ParseErrors(logText);
            var effectiveExitCode = testsExecuted ? exitCode : 1;
            await logStore.SaveAsync(
                definition.Id,
                BuildLogKind.Test,
                string.Join(" && ", commandLines),
                effectiveExitCode,
                startedAtUtc,
                logText,
                cancellationToken);

            if (effectiveExitCode == 0)
            {
                SetState(ProjectLifecycleState.TestOk);
            }
            else
            {
                buildErrorCount = Math.Max(parsed.ErrorCount, testsExecuted ? 0 : 1);
                buildWarningCount = BuildLogParser.ParseWarningCount(logText);
                lastErrorPreview = parsed.ErrorLines.FirstOrDefault()
                    ?? summaryLine
                    ?? "No tests were executed";
                SetState(ProjectLifecycleState.TestFailed);
            }
        }
        finally
        {
            Interlocked.Exchange(ref testInProgress, 0);
            fileChangeBuildCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(10);
            fileWatcher?.Resume();

            if (stoppedAppForTests
                && wasRunProcessActive
                && definition.RunOptions.RunMode != ProjectRunMode.None)
            {
                _ = RestartRunProcessAfterTestsAsync();
            }

            HealthCoalesceRequested?.Invoke(true);
        }
    }

    private sealed record TestTargetRunResult(CliRunResult Result, bool StoppedApp);

    private async Task RestartRunProcessAfterTestsAsync()
    {
        await Task.Delay(2500);

        if (Volatile.Read(ref testInProgress) != 0 || Volatile.Read(ref buildInProgress) != 0)
        {
            return;
        }

        StartRunProcess(skipEmbeddedBuild: true);
    }

    private async Task<TestTargetRunResult> RunTestTargetWithRetryAsync(
        string target,
        bool wasRunProcessActive,
        bool releaseLocksSetting,
        CancellationToken cancellationToken)
    {
        var stoppedApp = false;
        CliRunResult result;
        var usedNoBuild = false;

        if (TestRunPlanner.RequiresFullBuildFromStart(lastBuildExitCode))
        {
            stoppedApp = await StopAppForTestBuildIfNeededAsync(
                wasRunProcessActive,
                "stopping run/watch to rebuild before tests",
                cancellationToken);
            await ReleaseLocksForTestBuildIfNeededAsync(releaseLocksSetting, stoppedApp, cancellationToken);
            result = await RunTestAttemptAsync(BuildTestArgs(target, noBuild: false), cancellationToken);
        }
        else
        {
            AppendTestNote("running tests while app stays up (--no-build)");
            usedNoBuild = true;
            result = await RunTestAttemptAsync(BuildTestArgs(target, noBuild: true), cancellationToken);

            if (!DotNetTestOutputParser.LooksLikeTestsExecuted(result.Output)
                && DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(result.Output))
            {
                usedNoBuild = false;
                stoppedApp = await StopAppForTestBuildIfNeededAsync(
                    wasRunProcessActive,
                    "test assemblies stale — stopping app briefly to rebuild",
                    cancellationToken);
                await ReleaseLocksForTestBuildIfNeededAsync(releaseLocksSetting, stoppedApp, cancellationToken);
                result = await RunTestAttemptAsync(BuildTestArgs(target, noBuild: false), cancellationToken);
            }
        }

        var shouldReleaseLocks = TestRunPlanner.ShouldReleaseLocksForTestBuild(releaseLocksSetting, stoppedApp);
        var finalResult = await RetryTestOnLockErrorAsync(
            result,
            target,
            usedNoBuild,
            shouldReleaseLocks,
            wasRunProcessActive,
            cancellationToken);

        return new TestTargetRunResult(finalResult.Result, finalResult.StoppedApp || stoppedApp);
    }

    private async Task<bool> StopAppForTestBuildIfNeededAsync(
        bool wasRunProcessActive,
        string note,
        CancellationToken cancellationToken)
    {
        if (!wasRunProcessActive || runProcess?.IsRunning != true)
        {
            return false;
        }

        AppendTestNote(note);
        await StopRunProcessAsync(cancellationToken);
        return true;
    }

    private async Task ReleaseLocksForTestBuildIfNeededAsync(
        bool releaseLocksSetting,
        bool stoppedApp,
        CancellationToken cancellationToken)
    {
        if (!TestRunPlanner.ShouldReleaseLocksForTestBuild(releaseLocksSetting, stoppedApp))
        {
            return;
        }

        await ReleaseOutputLocksAsync(cancellationToken);
    }

    private async Task<TestTargetRunResult> RetryTestOnLockErrorAsync(
        CliRunResult result,
        string target,
        bool noBuild,
        bool shouldReleaseLocks,
        bool wasRunProcessActive,
        CancellationToken cancellationToken)
    {
        if (result.ExitCode == 0 || !BuildLogParser.IsOutputLockError(result.Output))
        {
            return new TestTargetRunResult(result, false);
        }

        var stoppedApp = false;
        if (shouldReleaseLocks || wasRunProcessActive)
        {
            stoppedApp = await StopAppForTestBuildIfNeededAsync(
                wasRunProcessActive,
                "output locked — stopping app before retrying tests",
                cancellationToken);
            AppendTestNote("output locked — releasing and retrying tests");
            await ReleaseOutputLocksAsync(cancellationToken);
            await Task.Delay(1000, cancellationToken);
            result = await RunTestAttemptAsync(BuildTestArgs(target, noBuild: false), cancellationToken);
        }

        return new TestTargetRunResult(result, stoppedApp);
    }

    private async Task<CliRunResult> RunTestAttemptAsync(
        List<string> args,
        CancellationToken cancellationToken) =>
        await cliRunner.RunAsync(
            definition.RootFolder,
            args,
            cancellationToken,
            OnTestOutputLine);

    private void AppendTestNote(string note)
    {
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine($"[BuildMonitor] {note}");
            liveTestOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
    }

    private string WriteTestStartBanner(string reason, TestTargetResolution resolution)
    {
        var banner = BuildMonitorLogBanner.FormatTest(Interlocked.Increment(ref testNumber), reason);
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine(banner);
            liveTestOutput.AppendLine($"[BuildMonitor] {resolution.DiscoveryNote}");
            if (resolution.Targets.Count == 1)
            {
                var tryNoBuild = lastBuildExitCode == 0;
                liveTestOutput.AppendLine(
                    $"dotnet {string.Join(' ', BuildTestArgs(resolution.Targets[0], tryNoBuild))}"
                    + (tryNoBuild ? " (app stays up; brief stop only if assemblies are stale)" : string.Empty));
            }

            liveTestOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
        return banner;
    }

    private void WriteTestStartBanner(string reason, IReadOnlyList<string> args, string note)
    {
        var banner = BuildMonitorLogBanner.FormatTest(Interlocked.Increment(ref testNumber), reason);
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine(banner);
            liveTestOutput.AppendLine($"[BuildMonitor] {note}");
            if (args.Count > 0)
            {
                liveTestOutput.AppendLine($"dotnet {string.Join(' ', args)}");
            }

            liveTestOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
    }

    private void AppendTestSectionHeader(int index, int total, string target)
    {
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine($"[BuildMonitor] --- Test target {index}/{total}: {target} ---");
            liveTestOutput.AppendLine($"dotnet {string.Join(' ', BuildTestArgs(target, lastBuildExitCode == 0))}");
            liveTestOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
    }

    private static string? DescribeMissingTestSummary(string logText, bool testsExecuted)
    {
        if (testsExecuted)
        {
            return null;
        }

        if (BuildLogParser.IsOutputLockError(logText))
        {
            return "build failed — app executable is locked; enable Stop processes locking build output in settings";
        }

        if (logText.Contains("No test is available", StringComparison.OrdinalIgnoreCase)
            || logText.Contains("No tests found", StringComparison.OrdinalIgnoreCase))
        {
            return "no tests discovered in target — set Test project / solution in settings";
        }

        if (DotNetTestOutputParser.LooksLikeRestoreOrBuildOnly(logText))
        {
            return "no tests executed (build did not reach test host) — check build errors above";
        }

        return "no tests executed";
    }

    private void OnTestOutputLine(string line)
    {
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine(line);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
        HeartbeatProjectWorker("test-output");
        RequestHealthCoalesce(immediate: false);
    }

    private void StartRunProcess(bool skipEmbeddedBuild = false)
    {
        SetProjectCurrentAction(skipEmbeddedBuild
            ? "Starting app (dotnet run --no-build)"
            : "Starting app (dotnet run)");
        StopRunProcess();
        WarnIfRiskyBaseOutputPath();

        runProcessGeneration++;
        var generation = runProcessGeneration;

        runProcess = new SupervisedProcess(definition.Id);
        runProcess.OutputLineReceived += OnRunProcessOutputLine;

        runProcessExitedHandler = (_, exitCode) =>
        {
            if (generation != runProcessGeneration)
            {
                return;
            }

            OnRunProcessExited(exitCode);
        };
        runProcess.Exited += runProcessExitedHandler;

        var args = UsesDotNetWatchProcess()
            ? BuildWatchArgs(skipEmbeddedBuild)
            : BuildRunArgs(skipEmbeddedBuild);

        candidateListenUrls = LaunchProfileEnvironmentApplier.ResolveListenUrls(
            definition.RootFolder,
            definition.ProjectFile,
            definition.LaunchProfile);
        pendingListenUrl = candidateListenUrls.FirstOrDefault();
        listenUrlReady = false;
        listenUrlNotified = false;
        runOutputSaveRevision = 0;
        StartListenUrlPolling();
        StartRunLogSaveTimer();

        runProcess.Start(
            definition.RootFolder,
            args,
            psi =>
            {
                LaunchProfileEnvironmentApplier.ApplyTo(
                    psi,
                    definition.RootFolder,
                    definition.ProjectFile,
                    definition.LaunchProfile);

                if (UsesDotNetWatchProcess()
                    && !definition.RunOptions.AutoRestartOnWatchChanges)
                {
                    psi.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "0";
                }
            });

        NotifyProgressChanged(force: true);

        SetState(definition.RunOptions.RunMode == ProjectRunMode.Watch
            || UsesCoalescedWatchRebuilds()
            ? ProjectLifecycleState.Watching
            : ProjectLifecycleState.Running);
    }

    private void OnRunProcessExited(int exitCode)
    {
        var exitedProcess = runProcess;
        if (exitedProcess is null)
        {
            return;
        }

        StopListenUrlPolling();
        StopRunLogSaveTimer();
        SaveRunOutputIfChanged(force: true);
        listenUrlReady = false;
        listenUrlNotified = false;
        lastExitCode = exitCode;
        var runOutput = exitedProcess.Output;
        runErrorCount = DotNetRunOutputParser.ParseErrorCount(runOutput);
        runWarningCount = DotNetRunOutputParser.ParseWarningCount(runOutput);
        if (exitCode != 0 && runErrorCount == 0)
        {
            runErrorCount = 1;
        }

        if (exitCode != 0 && definition.RunOptions.RestartOnCrash && restartCount < definition.RunOptions.MaxRestartRetries)
        {
            restartCount++;
            SetState(ProjectLifecycleState.Crashed);
            StartRunProcess(skipEmbeddedBuild: true);
            return;
        }

        if (exitCode != 0)
        {
            _ = logStore.SaveAsync(
                definition.Id,
                BuildLogKind.Run,
                exitedProcess.CommandLine,
                exitCode,
                DateTimeOffset.UtcNow,
                exitedProcess.Output,
                CancellationToken.None);
            SetState(ProjectLifecycleState.Crashed);
        }
        else
        {
            SetState(ProjectLifecycleState.Idle);
        }
    }

    private void StopRunProcess()
    {
        StopListenUrlPolling();
        StopRunLogSaveTimer();

        if (runProcess is null)
        {
            return;
        }

        SaveRunOutputIfChanged(force: true);
        runProcessGeneration++;
        DetachRunProcessHandlers();
        runProcess.Stop();
        runProcess = null;
    }

    private async Task StopRunProcessAsync(CancellationToken cancellationToken)
    {
        if (runProcess is null)
        {
            return;
        }

        runProcessGeneration++;
        DetachRunProcessHandlers();
        await runProcess.StopGracefullyAsync(cancellationToken);
        runProcess = null;
    }

    private void DetachRunProcessHandlers()
    {
        if (runProcess is null)
        {
            return;
        }

        runProcess.OutputLineReceived -= OnRunProcessOutputLine;
        if (runProcessExitedHandler is not null)
        {
            runProcess.Exited -= runProcessExitedHandler;
            runProcessExitedHandler = null;
        }
    }

    private List<string> BuildProjectArgs()
    {
        var args = new List<string> { "build", ResolveProjectFileArg() };
        AppendExtraArgs(args);
        return args;
    }

    private List<string> BuildTestArgs(string testTargetPath, bool noBuild = false)
    {
        var args = new List<string>
        {
            "test",
            testTargetPath,
            "--verbosity",
            "normal",
            "--logger",
            "console;verbosity=detailed"
        };

        if (noBuild)
        {
            args.Add("--no-build");
        }

        AppendExtraArgs(args);
        return args;
    }

    private List<string> BuildRunArgs(bool skipEmbeddedBuild = false)
    {
        var args = new List<string> { "run", "--project", ResolveProjectFileArg() };
        if (skipEmbeddedBuild)
        {
            args.Add("--no-build");
        }

        if (!string.IsNullOrWhiteSpace(definition.LaunchProfile))
        {
            args.AddRange(["--launch-profile", definition.LaunchProfile]);
        }

        AppendExtraArgs(args);
        return args;
    }

    private List<string> BuildWatchArgs(bool skipEmbeddedBuild = false)
    {
        var args = new List<string> { "watch" };
        if (definition.RunOptions.AutoRestartOnWatchChanges)
        {
            // Tray host has no stdin for restart prompts — auto-restart when enabled per project.
            args.Add("--non-interactive");
        }

        args.AddRange(["run", "--project", ResolveProjectFileArg()]);
        if (skipEmbeddedBuild)
        {
            args.Add("--no-build");
        }

        if (!string.IsNullOrWhiteSpace(definition.LaunchProfile))
        {
            args.AddRange(["--launch-profile", definition.LaunchProfile]);
        }

        AppendExtraArgs(args);
        return args;
    }

    private void AppendExtraArgs(List<string> args)
    {
        if (string.IsNullOrWhiteSpace(definition.ExtraDotNetArgs))
        {
            return;
        }

        args.AddRange(definition.ExtraDotNetArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private string ResolveProjectFileArg() =>
        Path.IsPathRooted(definition.ProjectFile)
            ? definition.ProjectFile
            : Path.Combine(definition.RootFolder, definition.ProjectFile);

    private void SetState(ProjectLifecycleState newState)
    {
        state = newState;
        lastChangedUtc = DateTimeOffset.UtcNow;
        var action = FormatLifecycleAction(newState);
        SetProjectCurrentAction(action);
        HeartbeatProjectWorker("state", newState.ToString());
        RefreshHealth();
        HealthCoalesceRequested?.Invoke(true);
    }

    private static string FormatLifecycleAction(ProjectLifecycleState state) =>
        state switch
        {
            ProjectLifecycleState.Idle => "Idle",
            ProjectLifecycleState.Building => "Building",
            ProjectLifecycleState.BuildOk => "Build succeeded",
            ProjectLifecycleState.BuildFailed => "Build failed",
            ProjectLifecycleState.Running => "App running",
            ProjectLifecycleState.Watching => "Watching for file changes",
            ProjectLifecycleState.Crashed => "App crashed",
            ProjectLifecycleState.Testing => "Running tests",
            ProjectLifecycleState.TestOk => "Tests passed",
            ProjectLifecycleState.TestFailed => "Tests failed",
            _ => state.ToString()
        };

    private void RefreshHealth()
    {
        var (displayErrors, displayWarnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            state,
            buildErrorCount,
            buildWarningCount,
            runErrorCount,
            runWarningCount);
        health = ProjectHealthEvaluator.Evaluate(
            state,
            lastBuildExitCode,
            displayErrors,
            displayWarnings,
            inProgress: isRestarting
                || state is ProjectLifecycleState.Building
                || state is ProjectLifecycleState.Testing);
    }

    private string? ResolveDisplayListenUrl()
    {
        if (definition.RunOptions.RunMode == ProjectRunMode.None)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(pendingListenUrl))
        {
            return pendingListenUrl;
        }

        if (candidateListenUrls.Count > 0)
        {
            return candidateListenUrls[0];
        }

        return LaunchProfileEnvironmentApplier.ResolvePrimaryListenUrl(
            definition.RootFolder,
            definition.ProjectFile,
            definition.LaunchProfile);
    }

    private void RefreshListenUrlReady()
    {
        if (runProcess?.IsRunning != true
            || state is not (ProjectLifecycleState.Running or ProjectLifecycleState.Watching))
        {
            listenUrlReady = false;
            return;
        }

        var urlsToProbe = candidateListenUrls.Count > 0
            ? candidateListenUrls
            : string.IsNullOrWhiteSpace(pendingListenUrl) ? [] : new[] { pendingListenUrl };

        foreach (var url in urlsToProbe)
        {
            if (LocalPortProbe.IsHttpEndpointOpen(url))
            {
                MarkListenUrlReady(url);
                return;
            }
        }

        listenUrlReady = false;
    }

    private void StartListenUrlPolling()
    {
        StopListenUrlPolling();
        listenUrlPollTimer = new Timer(
            _ => PollListenUrl(),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private void StopListenUrlPolling()
    {
        listenUrlPollTimer?.Dispose();
        listenUrlPollTimer = null;
    }

    private void PollListenUrl()
    {
        if (listenUrlReady)
        {
            return;
        }

        RefreshListenUrlReady();
    }

    private void MarkListenUrlReady(string url)
    {
        pendingListenUrl = url;
        if (listenUrlReady)
        {
            return;
        }

        listenUrlReady = true;
        StopListenUrlPolling();
        NotifyProgressChanged(force: true);

        if (listenUrlNotified)
        {
            return;
        }

        listenUrlNotified = true;
        var openUrl = LocalPortProbe.NormalizeBrowserUrl(url);
        notifyUser?.Invoke(
            definition.Id,
            $"App running — {definition.DisplayName}",
            $"Open {openUrl}",
            UserNotificationKind.Info,
            UserNotificationCategory.Info);
    }

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

    public Task StopAsync()
    {
        fileWatcher?.Dispose();
        fileWatcher = null;
        StopListenUrlPolling();
        StopRunProcess();
        buildProgressTracker = null;
        progressSteps = [];
        SetState(ProjectLifecycleState.Idle);
        return Task.CompletedTask;
    }

    private void RecordBuildTrigger(
        BuildTriggerKind kind,
        string summary,
        string? detail,
        IReadOnlyList<string>? changedPaths = null)
    {
        triggerJournal.Record(new BuildTriggerRecord(
            Guid.NewGuid().ToString("N"),
            definition.Id,
            definition.DisplayName,
            DateTimeOffset.UtcNow,
            kind,
            summary,
            detail,
            changedPaths is { Count: > 0 } ? changedPaths : null,
            InferredCause: BuildTriggerInference.Infer(kind, detail, changedPaths)));
    }

    private IReadOnlyList<string> RelativizePaths(IReadOnlyList<string> fullPaths)
    {
        if (fullPaths.Count == 0)
        {
            return [];
        }

        var root = Path.GetFullPath(definition.RootFolder);
        var results = new List<string>(fullPaths.Count);
        foreach (var path in fullPaths)
        {
            try
            {
                var full = Path.GetFullPath(path);
                results.Add(Path.GetRelativePath(root, full));
            }
            catch
            {
                results.Add(path);
            }
        }

        return results;
    }

    public void Dispose()
    {
        UnregisterProjectWorkers();
        StopListenUrlPolling();
        StopRunLogSaveTimer();
        fileWatcher?.Dispose();
        runProcess?.Dispose();
    }

    private string ProjectWorkerId(string suffix) => $"project.{definition.Id}.{suffix}";

    private void RegisterProjectWorkers()
    {
        var registry = WorkerHealthRegistry.Shared;
        void Register(string suffix, string label, TimeSpan staleAfter)
        {
            var id = ProjectWorkerId(suffix);
            registry.Register(id, $"{definition.DisplayName} — {label}", staleAfter, "Project");
            registeredWorkerIds.Add(id);
        }

        Register("build-output", "build output", TimeSpan.FromSeconds(5));
        Register("run-output", "run output", TimeSpan.FromSeconds(10));
        Register("test-output", "test output", TimeSpan.FromSeconds(10));
        Register("file-watcher", "file watcher", TimeSpan.FromMinutes(30));
        Register("state", "lifecycle", TimeSpan.FromMinutes(10));
    }

    private void UnregisterProjectWorkers()
    {
        var registry = WorkerHealthRegistry.Shared;
        foreach (var id in registeredWorkerIds)
        {
            registry.Unregister(id);
        }

        registeredWorkerIds.Clear();
        lastWorkerHeartbeatUtc.Clear();
    }

    private void SetProjectCurrentAction(string action)
    {
        WorkerHealthRegistry.Shared.SetCurrentAction(ProjectWorkerId("state"), action);
    }

    private void HeartbeatProjectWorker(string suffix, string? note = null)
    {
        var id = ProjectWorkerId(suffix);
        var now = DateTimeOffset.UtcNow;
        if (lastWorkerHeartbeatUtc.TryGetValue(id, out var last)
            && (now - last).TotalMilliseconds < 500)
        {
            return;
        }

        lastWorkerHeartbeatUtc[id] = now;
        WorkerHealthRegistry.Shared.Heartbeat(
            id,
            note,
            Environment.CurrentManagedThreadId);
    }
}
