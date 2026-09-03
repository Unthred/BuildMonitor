using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;


internal sealed partial class ProjectRuntime : IDisposable
{
    private readonly BuildLogStore logStore;
    private readonly BuildTriggerJournal triggerJournal;
    private readonly FileChangeBurstStatsStore burstStatsStore;
    private readonly BuildTrainingStore trainingStore;
    private readonly DotNetCliRunner cliRunner;
    private Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifyUser;
    private SupervisedProcess? runProcess;
    private DebouncedFileWatcher? fileWatcher;
    private MonitoredProjectSettings projectSettings;
    private LocalProjectAttachment Local =>
        projectSettings.Local
        ?? throw new InvalidOperationException($"Project '{projectSettings.Id}' has no Local attachment.");
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
    private int compileInProgress;
    private string? currentBuildTriggerId;
    private int buildTriggeredByFileChange;
    private bool pendingFileChangeRebuild;
    private DateTimeOffset fileChangeBuildCooldownUntil = DateTimeOffset.MinValue;
    private DateTimeOffset lastWatchFileChangeNotifyUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastHotReloadRestartRequestUtc = DateTimeOffset.MinValue;
    private int fileChangeDebounceMs = 3000;
    private int manualFileChangeDebounceMs = 3000;
    private FileChangeDebounceMode debounceMode = FileChangeDebounceMode.Manual;
    private bool coalesceWatchRebuilds = true;
    private bool learnFromDiagnosticsVerdicts = true;
    private int pendingHotReloadRestartRequest;
    private int buildNumber;
    private string pendingBuildReason = "startup";
    private DateTimeOffset lastMeaningfulFileChangeUtc = DateTimeOffset.MinValue;
    private int fileChangeRebuildScheduleGeneration;
    private readonly Queue<DateTimeOffset> recentFileChangeBuildStarts = new();
    private IReadOnlyList<string> lastFileChangePaths = [];
    private string? lastFileChangeTriggerDetail;
    private PendingRebuildHoldReason pendingRebuildHoldReason;
    private int pendingRebuildHoldFileCount;
    private IReadOnlyList<string> pendingRebuildHoldSamplePaths = [];
    private int pendingRebuildTimerResetCount;
    private int runProcessGeneration;
    private DesiredRunHostState desiredRunHostState = DesiredRunHostState.Stopped;
    private Action<string, int>? runProcessExitedHandler;
    private string? pendingListenUrl;
    private IReadOnlyList<string> candidateListenUrls = [];
    private bool listenUrlReady;
    private bool listenUrlNotified;
    private DateTimeOffset? listenUrlFirstOpenUtc;
    private int runOutputSaveRevision;
    private Timer? listenUrlPollTimer;
    private Timer? runLogSaveTimer;

    private int healthDirty;

    // Lifecycle probes for #90 remount-without-build regression tests.
    private int buildAsyncInvocationCount;
    private int remountWithoutBuildCount;
    private int watcherCreateCount;
    private int processStartCount;

    private readonly List<string> registeredWorkerIds = [];
    private readonly Dictionary<string, DateTimeOffset> lastWorkerHeartbeatUtc = new(StringComparer.OrdinalIgnoreCase);

    public event Action<bool>? HealthCoalesceRequested;

    public string ProjectId => projectSettings.Id;
    public string DisplayName => projectSettings.DisplayName;
    public bool IsRunProcessActive => runProcess?.IsRunning == true;
    public bool RestartAppAfterRebuild => Local.RunOptions.RestartAppAfterRebuild;

    /// <summary>
    /// Desired supervised host state (Running vs Stopped). Separate from temporary operational pause.
    /// </summary>
    public DesiredRunHostState DesiredRunHostState => desiredRunHostState;

    /// <summary>Test probe: how many times <see cref="BuildAsync"/> was entered.</summary>
    public int BuildAsyncInvocationCount => Volatile.Read(ref buildAsyncInvocationCount);

    /// <summary>Test probe: Settings remount-without-build entries.</summary>
    public int RemountWithoutBuildCount => Volatile.Read(ref remountWithoutBuildCount);

    /// <summary>Test probe: file watcher constructions.</summary>
    public int WatcherCreateCount => Volatile.Read(ref watcherCreateCount);

    /// <summary>Test probe: supervised process start attempts.</summary>
    public int ProcessStartCount => Volatile.Read(ref processStartCount);

    public ProjectHealthSnapshot Snapshot => BuildSnapshot();

    public ProjectHealthSnapshot BuildSnapshot()
    {
        RefreshHealth();
        var (displayErrors, displayWarnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
                state,
                buildErrorCount,
                buildWarningCount,
                runErrorCount,
                runWarningCount,
                lastBuildExitCode);
            return new ProjectHealthSnapshot(
                projectSettings.Id,
                projectSettings.DisplayName,
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
                projectSettings.IsActiveInSession,
                progressSteps.Count == 0 ? progressSteps : progressSteps.ToArray(),
                ResolveDisplayListenUrl(),
                listenUrlReady,
                Local.RunOptions.RunMode != ProjectRunMode.None,
                HealthIssueCountsFormatter.FormatStatusLine(
                    state,
                    buildErrorCount,
                    buildWarningCount,
                    runErrorCount,
                    runWarningCount,
                    lastBuildExitCode),
                HealthIssueCountsFormatter.FormatFailurePhase(state, lastBuildExitCode),
                isRestarting,
                IsEditGatingActive(),
                BuildEditGatingDetailText(),
                GetEditGatingQuietUntilUtc(),
                lastBuildExitCode,
                BuildControlPlaneSnapshot());
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
        RefreshControlPlaneHealthIfNeeded();
        RefreshLiveIssueCounts(force: true);
        RefreshHealth();
    }

    private void RequestHealthCoalesce(bool immediate = false)
    {
        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(immediate);
    }

    public ProjectRuntime(
        MonitoredProjectSettings projectSettings,
        BuildLogStore logStore,
        DotNetCliRunner cliRunner,
        BuildTriggerJournal triggerJournal,
        FileChangeBurstStatsStore burstStatsStore,
        BuildTrainingStore trainingStore,
        Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifyUser = null)
    {
        if (projectSettings.Local is null)
        {
            throw new ArgumentException("ProjectRuntime requires a Local attachment.", nameof(projectSettings));
        }

        this.projectSettings = projectSettings;
        this.logStore = logStore;
        this.cliRunner = cliRunner;
        this.triggerJournal = triggerJournal;
        this.burstStatsStore = burstStatsStore;
        this.trainingStore = trainingStore;
        this.notifyUser = notifyUser;
        RegisterProjectWorkers();
    }

    public void UpdateDefinition(MonitoredProjectSettings updated, GlobalMonitorSettings? monitor = null)
    {
        if (updated.Local is null)
        {
            throw new ArgumentException("ProjectRuntime requires a Local attachment.", nameof(updated));
        }

        projectSettings = updated;
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
        learnFromDiagnosticsVerdicts = monitor.LearnFromDiagnosticsVerdicts;
        ApplyMonitorSuppressionSettings(monitor);
    }

    private HashSet<string> GetEffectiveWatchIgnoreSegments() =>
        WatchExcludeSegments.ResolveIgnoreSegmentSet(
            Local.RunOptions.WatchExcludeSegments,
            trainingStore.GetLearnedExcludeSegments(projectSettings.Id));

    public void RefreshWatchIgnoreSegments(IEnumerable<string> segments) =>
        fileWatcher?.AddIgnoreSegments(segments);

    public void RefreshFileWatcherDebounce() => SyncFileWatcherDebounceMs();

    private int ResolveFileChangeDebounceMs() =>
        AdaptiveFileChangeDebounce.ResolveEffectiveDebounce(
            debounceMode,
            manualFileChangeDebounceMs,
            burstStatsStore.GetOrDefault(projectSettings.Id));

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

    public BuildIntelligenceSnapshot GetIntelligenceSnapshot(GlobalMonitorSettings monitor, int todayTriggerCount = 0)
    {
        PruneRecentFileChangeBuildStarts();
        var stats = burstStatsStore.GetOrDefault(projectSettings.Id);
        var liveDebounceMs = GetSessionAdjustedFileChangeDebounceMs();
        DateTimeOffset? rebuildQuietUntilUtc = pendingFileChangeRebuild
                                               && lastMeaningfulFileChangeUtc != DateTimeOffset.MinValue
            ? AdaptiveFileChangeDebounce.ComputeQuietUntilUtc(
                lastMeaningfulFileChangeUtc,
                liveDebounceMs)
            : null;

        return BuildIntelligenceSnapshot.Create(
            projectSettings,
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
            rebuildQuietUntilUtc,
            todayTriggerCount,
            pendingRebuildHoldReason,
            pendingRebuildHoldFileCount,
            pendingRebuildHoldSamplePaths,
            pendingRebuildTimerResetCount);
    }

    private bool UsesCoalescedWatchRebuilds() =>
        Local.RunOptions.RunMode == ProjectRunMode.Watch
        && coalesceWatchRebuilds
        && Local.BuildControlMode != ProjectBuildControlMode.AiControlled;

    /// <summary>
    /// AI Controlled never hosts <c>dotnet watch</c> — file changes must not compile inside the watch process.
    /// Use <c>dotnet run --no-build</c> so the app stays up until an explicit rebuild.
    /// </summary>
    private bool UsesDotNetWatchProcess() =>
        Local.BuildControlMode != ProjectBuildControlMode.AiControlled
        && Local.RunOptions.RunMode == ProjectRunMode.Watch
        && !UsesCoalescedWatchRebuilds();

    private bool ShouldStartFileWatcher()
    {
        if (Local.RunOptions.FileChanges == FileChangeMode.Off)
        {
            return false;
        }

        // Always observe in AI Controlled (counts/status) unless file watching is fully off.
        if (Local.BuildControlMode == ProjectBuildControlMode.AiControlled)
        {
            return Local.RunOptions.FileChanges != FileChangeMode.Off
                || Local.RunOptions.RunMode != ProjectRunMode.None;
        }

        if (UsesCoalescedWatchRebuilds())
        {
            return true;
        }

        return Local.RunOptions.FileChanges == FileChangeMode.TriggerRebuild
            && Local.RunOptions.RunMode != ProjectRunMode.Watch;
    }

    public void SetUserNotifier(Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifier) =>
        notifyUser = notifier;

    private (int Errors, int Warnings) CountLiveBuildIssues(string normalized) =>
        BuildIssueCountResolver.Resolve(
            normalized,
            logStore.GetLogPath(projectSettings.Id, BuildLogKind.Build));

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
        var isDirectBuild = Volatile.Read(ref compileInProgress) != 0
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
        var (liveErrors, liveWarnings) = kind == BuildLogKind.Build
            ? CountLiveBuildIssues(normalized)
            : CountLiveIssues(kind, normalized);
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
        // Cold StartOnLaunch path: session wants the host running after startup freshness work.
        desiredRunHostState = DesiredRunHostState.Running;
        SetProjectCurrentAction("Starting — loading saved build state");
        await HydrateLastBuildFromStoreAsync(cancellationToken);
        TryStartAgentActivityWatcher();

        var activity = EvaluateEditActivity();
        if (BuildSuppressionPolicy.ShouldDeferStartupBuild(GetSuppressionSettings(), activity))
        {
            pendingFileChangeRebuild = true;
            QueuePendingRebuild(
                PendingRebuildHoldReason.StartupDeferred,
                [],
                wasAlreadyPending: false);
            await WaitForEditQuietThenBuildAsync("startup");
        }
        else
        {
            await BuildAsync(cancellationToken);
        }

        if (Local.RunOptions.RunMode == ProjectRunMode.None)
        {
            TryStartFileWatcher();
            return;
        }

        if (lastBuildExitCode != 0)
        {
            TryStartFileWatcher();
            RefreshHealth();
            return;
        }

        // BuildAsync may already have started run when RestartAppAfterRebuild is enabled.
        if (runProcess?.IsRunning != true)
        {
            StartRunProcess(skipEmbeddedBuild: true);
        }

        TryStartFileWatcher();
    }

    /// <summary>
    /// Remount watcher/process after a Hard Settings Save. Structurally does not call
    /// <see cref="BuildAsync"/> — Settings remount is never a build trigger.
    /// </summary>
    public async Task RemountWithoutBuildAsync(LocalRemountKind kind, CancellationToken cancellationToken)
    {
        if (kind is LocalRemountKind.None or LocalRemountKind.StopOnly)
        {
            return;
        }

        Interlocked.Increment(ref remountWithoutBuildCount);
        SetProjectCurrentAction("Remounting runtime (no build)");

        switch (kind)
        {
            case LocalRemountKind.WatcherOnly:
                RemountFileWatcherOnly();
                break;

            case LocalRemountKind.MountFresh:
                await StopRunProcessAsync(cancellationToken).ConfigureAwait(false);
                RemountFileWatcherOnly();
                SetState(fileWatcher is null ? ProjectLifecycleState.Idle : ProjectLifecycleState.Watching);
                break;

            case LocalRemountKind.SourceIdentity:
                await StopRunProcessAsync(cancellationToken).ConfigureAwait(false);
                lastBuildExitCode = -1;
                lastBuildFinishedAtUtc = null;
                RemountFileWatcherOnly();
                SetState(fileWatcher is null ? ProjectLifecycleState.Idle : ProjectLifecycleState.Watching);
                break;

            case LocalRemountKind.ProcessAndWatcher:
                await StopRunProcessAsync(cancellationToken).ConfigureAwait(false);
                RemountFileWatcherOnly();
                if (Local.RunOptions.RunMode == ProjectRunMode.None)
                {
                    SetState(fileWatcher is null ? ProjectLifecycleState.Idle : ProjectLifecycleState.Watching);
                    break;
                }

                if (lastBuildExitCode == 0)
                {
                    StartRunProcess(skipEmbeddedBuild: true);
                }
                else
                {
                    SetState(fileWatcher is null ? ProjectLifecycleState.Idle : ProjectLifecycleState.Watching);
                }

                break;

            default:
                break;
        }

        RefreshHealth();
        HealthCoalesceRequested?.Invoke(true);
    }

    private void RemountFileWatcherOnly()
    {
        var wasRunning = runProcess?.IsRunning == true;
        fileWatcher?.Dispose();
        fileWatcher = null;
        TryStartFileWatcher();
        if (wasRunning && runProcess?.IsRunning == true)
        {
            // Process intentionally left running across watcher-only remount.
            SetState(Local.RunOptions.RunMode == ProjectRunMode.Watch
                ? ProjectLifecycleState.Watching
                : ProjectLifecycleState.Running);
        }
        else if (runProcess?.IsRunning != true && fileWatcher is not null
                 && Local.RunOptions.RunMode == ProjectRunMode.None)
        {
            SetState(ProjectLifecycleState.Watching);
        }
    }
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
            ProjectLifecycleState.WaitingForEdits => "Waiting for edits to settle",
            _ => state.ToString()
        };

    private void RefreshHealth()
    {
        var (displayErrors, displayWarnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            state,
            buildErrorCount,
            buildWarningCount,
            runErrorCount,
            runWarningCount,
            lastBuildExitCode);
        health = ProjectHealthEvaluator.Evaluate(
            state,
            lastBuildExitCode,
            displayErrors,
            displayWarnings,
            inProgress: isRestarting
                || state is ProjectLifecycleState.Building
                || state is ProjectLifecycleState.Testing
                || state is ProjectLifecycleState.WaitingForEdits);
    }
    private void RecordBuildTrigger(
        BuildTriggerKind kind,
        string summary,
        string? detail,
        IReadOnlyList<string>? changedPaths = null)
    {
        var id = Guid.NewGuid().ToString("N");
        if (Volatile.Read(ref buildInProgress) != 0)
        {
            currentBuildTriggerId = id;
        }

        triggerJournal.Record(new BuildTriggerRecord(
            id,
            projectSettings.Id,
            projectSettings.DisplayName,
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

        var root = Path.GetFullPath(Local.RootFolder);
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
        agentActivityWatcher?.Dispose();
        runProcess?.Dispose();
    }

    private string ProjectWorkerId(string suffix) => $"project.{projectSettings.Id}.{suffix}";

    private void RegisterProjectWorkers()
    {
        var registry = WorkerHealthRegistry.Shared;
        void Register(string suffix, string label, TimeSpan staleAfter)
        {
            var id = ProjectWorkerId(suffix);
            registry.Register(id, $"{projectSettings.DisplayName} — {label}", staleAfter, "Project");
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
