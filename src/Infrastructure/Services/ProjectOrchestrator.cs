using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

public sealed class ProjectOrchestrator : IDisposable
{
    private readonly DotNetCliRunner cliRunner = new();
    private readonly BuildLogStore logStore;
    private readonly Dictionary<string, ProjectRuntime> runtimes = new();
    private readonly object sync = new();
    private AppSettings settings = new();

    public event Action<IReadOnlyList<ProjectHealthSnapshot>, MonitorHealth>? HealthUpdated;
    public event Action<string, string, string, UserNotificationKind, UserNotificationCategory>? UserNotification;

    public ProjectOrchestrator(string logsRootDirectory) =>
        logStore = new BuildLogStore(logsRootDirectory);

    public BuildLogStore LogStore => logStore;

    public LiveBuildLogView? GetLiveBuildLog(string projectId, BuildLogKind kind)
    {
        lock (sync)
        {
            return runtimes.TryGetValue(projectId, out var runtime)
                ? runtime.GetLiveBuildLogView(kind)
                : null;
        }
    }

    public void ApplySettings(AppSettings newSettings)
    {
        List<string> idsToStop;
        lock (sync)
        {
            settings = newSettings;
            var activeIds = newSettings.Projects
                .Where(p => p.IsActiveInSession)
                .Select(p => p.Id)
                .ToHashSet();

            idsToStop = runtimes.Keys.Where(id => !activeIds.Contains(id)).ToList();

            foreach (var project in newSettings.Projects.Where(p => p.IsActiveInSession))
            {
                if (!runtimes.ContainsKey(project.Id))
                {
                    var runtime = new ProjectRuntime(project, logStore, cliRunner, RaiseUserNotification);
                    runtime.HealthChanged += OnRuntimeHealthChanged;
                    runtimes[project.Id] = runtime;
                }
                else
                {
                    runtimes[project.Id].UpdateDefinition(project, newSettings.Monitor.FileChangeDebounceMs);
                }

                runtimes[project.Id].SetUserNotifier(RaiseUserNotification);
            }
        }

        foreach (var id in idsToStop)
        {
            StopProject(id);
        }

        PublishHealth();
    }

    public async Task StartActiveProjectsAsync(CancellationToken cancellationToken)
    {
        List<ProjectRuntime> active;
        lock (sync)
        {
            active = runtimes.Values.ToList();
        }

        foreach (var runtime in active.Take(settings.Monitor.MaxConcurrentActiveProjects))
        {
            try
            {
                await runtime.StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                RaiseUserNotification(
                    runtime.ProjectId,
                    $"Failed to start {runtime.DisplayName}",
                    ExceptionDetailFormatter.Format(ex),
                    UserNotificationKind.Error,
                    UserNotificationCategory.Error);
            }
        }
    }

    public async Task StopAllAsync()
    {
        List<ProjectRuntime> all;
        lock (sync)
        {
            all = runtimes.Values.ToList();
        }

        foreach (var runtime in all)
        {
            await runtime.StopAsync();
        }
    }

    public async Task RebuildAsync(string projectId, CancellationToken cancellationToken)
    {
        if (!runtimes.TryGetValue(projectId, out var runtime))
        {
            return;
        }

        try
        {
            runtime.PrepareBuild("manual rebuild");
            await runtime.BuildAsync(cancellationToken);
            if (runtime.RestartAppAfterRebuild)
            {
                runtime.EnsureRunProcessStartedAfterBuild();
            }

            PublishHealth();
        }
        catch (Exception ex)
        {
            RaiseUserNotification(
                runtime.ProjectId,
                $"Rebuild failed — {runtime.DisplayName}",
                ExceptionDetailFormatter.Format(ex),
                UserNotificationKind.Error,
                UserNotificationCategory.BuildFailure);
        }
    }

    public async Task RunTestsAsync(string projectId, CancellationToken cancellationToken)
    {
        if (!runtimes.TryGetValue(projectId, out var runtime))
        {
            return;
        }

        try
        {
            runtime.PrepareTest("manual");
            await runtime.TestAsync(cancellationToken);
            PublishHealth();
        }
        catch (Exception ex)
        {
            RaiseUserNotification(
                runtime.ProjectId,
                $"Tests failed — {runtime.DisplayName}",
                ExceptionDetailFormatter.Format(ex),
                UserNotificationKind.Error,
                UserNotificationCategory.Error);
        }
    }

    public async Task RestartAppAsync(string projectId, CancellationToken cancellationToken)
    {
        if (!runtimes.TryGetValue(projectId, out var runtime))
        {
            return;
        }

        try
        {
            await runtime.RestartAppAsync(cancellationToken);
            PublishHealth();
        }
        catch (Exception ex)
        {
            RaiseUserNotification(
                runtime.ProjectId,
                $"Restart failed — {runtime.DisplayName}",
                ExceptionDetailFormatter.Format(ex),
                UserNotificationKind.Error,
                UserNotificationCategory.Error);
        }
    }

    public async Task StopProjectAsync(string projectId)
    {
        ProjectRuntime? runtime;
        lock (sync)
        {
            if (!runtimes.TryGetValue(projectId, out runtime))
            {
                return;
            }

            runtimes.Remove(projectId);
        }

        await runtime.StopAsync();
        runtime.Dispose();
        PublishHealth();
    }

    public void StopProject(string projectId)
    {
        // Synchronous wrapper for callers that cannot await; never call while holding sync.
        StopProjectAsync(projectId).GetAwaiter().GetResult();
    }

    public IReadOnlyList<ProjectHealthSnapshot> GetHealthSnapshots()
    {
        lock (sync)
        {
            var active = runtimes.Values.Select(r => r.Snapshot).ToList();
            var inactive = settings.Projects
                .Where(p => !p.IsActiveInSession)
                .Select(p => new ProjectHealthSnapshot(
                    p.Id,
                    p.DisplayName,
                    MonitorHealth.Unknown,
                    ProjectHealthEvaluator.ToLabel(MonitorHealth.Unknown),
                    ProjectLifecycleState.Idle,
                    null,
                    null,
                    null,
                    0,
                    0,
                    DateTimeOffset.MinValue,
                    null,
                    false,
                    [],
                    null,
                    false,
                    p.RunOptions.RunMode != ProjectRunMode.None));

            return active.Concat(inactive).ToList();
        }
    }

    private void OnRuntimeHealthChanged() => PublishHealth();

    private void RaiseUserNotification(
        string projectId,
        string title,
        string message,
        UserNotificationKind kind,
        UserNotificationCategory category) =>
        UserNotification?.Invoke(projectId, title, message, kind, category);

    private void PublishHealth()
    {
        var snapshots = GetHealthSnapshots();
        var activeOnly = snapshots.Where(s => s.IsActive).ToList();
        var rollup = LocalTrayIconRollupEvaluator.Rollup(activeOnly);
        HealthUpdated?.Invoke(snapshots, rollup);
    }

    public void Dispose()
    {
        foreach (var runtime in runtimes.Values)
        {
            runtime.Dispose();
        }

        runtimes.Clear();
    }
}

internal sealed class ProjectRuntime : IDisposable
{
    private readonly BuildLogStore logStore;
    private readonly DotNetCliRunner cliRunner;
    private Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifyUser;
    private SupervisedProcess? runProcess;
    private DebouncedFileWatcher? fileWatcher;
    private LocalProjectDefinition definition;
    private ProjectLifecycleState state = ProjectLifecycleState.Idle;
    private MonitorHealth health = MonitorHealth.Unknown;
    private int restartCount;
    private string? lastErrorPreview;
    private int errorCount;
    private int warningCount;
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
    private DateTimeOffset lastProgressPublishUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastLiveCountParseUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<BuildProgressStep> progressSteps = [];
    private BuildProgressTracker? buildProgressTracker;
    private int buildInProgress;
    private int buildTriggeredByFileChange;
    private bool pendingFileChangeRebuild;
    private DateTimeOffset fileChangeBuildCooldownUntil = DateTimeOffset.MinValue;
    private DateTimeOffset lastWatchFileChangeNotifyUtc = DateTimeOffset.MinValue;
    private int fileChangeDebounceMs = 1500;
    private int buildNumber;
    private string pendingBuildReason = "startup";
    private int runProcessGeneration;
    private Action<string, int>? runProcessExitedHandler;
    private string? pendingListenUrl;
    private IReadOnlyList<string> candidateListenUrls = [];
    private bool listenUrlReady;
    private bool listenUrlNotified;
    private int runOutputSaveRevision;
    private Timer? listenUrlPollTimer;
    private Timer? runLogSaveTimer;

    public event Action? HealthChanged;

    public string ProjectId => definition.Id;
    public string DisplayName => definition.DisplayName;
    public bool RestartAppAfterRebuild => definition.RunOptions.RestartAppAfterRebuild;

    public ProjectHealthSnapshot Snapshot
    {
        get
        {
            RefreshHealth();
            RefreshListenUrlReady();
            return new ProjectHealthSnapshot(
                definition.Id,
                definition.DisplayName,
                health,
                ProjectHealthEvaluator.ToLabel(health),
                state,
                lastExitCode,
                lastDuration,
                lastErrorPreview,
                errorCount,
                warningCount,
                lastChangedUtc,
                lastBuildFinishedAtUtc,
                definition.IsActiveInSession,
                progressSteps,
                pendingListenUrl,
                listenUrlReady,
                definition.RunOptions.RunMode != ProjectRunMode.None);
        }
    }

    public ProjectRuntime(
        LocalProjectDefinition definition,
        BuildLogStore logStore,
        DotNetCliRunner cliRunner,
        Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifyUser = null)
    {
        this.definition = definition;
        this.logStore = logStore;
        this.cliRunner = cliRunner;
        this.notifyUser = notifyUser;
    }

    public void UpdateDefinition(LocalProjectDefinition updated, int? debounceMs = null)
    {
        definition = updated;
        if (debounceMs is > 0)
        {
            fileChangeDebounceMs = debounceMs.Value;
        }
    }

    public void SetUserNotifier(Action<string, string, string, UserNotificationKind, UserNotificationCategory>? notifier) =>
        notifyUser = notifier;

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
        return new LiveBuildLogView(
            normalized,
            true,
            state,
            BuildLogParser.ParseErrorCount(normalized),
            BuildLogParser.ParseWarningCount(normalized),
            revision);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
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

        if (definition.RunOptions.FileChanges == FileChangeMode.TriggerRebuild
            && definition.RunOptions.RunMode != ProjectRunMode.Watch)
        {
            try
            {
                fileWatcher = new DebouncedFileWatcher(definition.RootFolder, fileChangeDebounceMs);
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
    }

    public async Task BuildAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref buildInProgress, 1, 0) != 0)
        {
            return;
        }

        var shouldRestartRun = runProcess?.IsRunning == true;
        var triggeredByFileChange = Volatile.Read(ref buildTriggeredByFileChange) != 0;
        var buildReason = triggeredByFileChange ? "file change" : pendingBuildReason;
        pendingBuildReason = "startup";

        fileWatcher?.Suspend();

        try
        {
            if (runProcess is not null)
            {
                await StopRunProcessAsync(cancellationToken);
                await Task.Delay(500, cancellationToken);
            }

            lock (liveOutputSync)
            {
                liveBuildOutput.Clear();
            }

            watchRebuildInProgress = false;
            Interlocked.Exchange(ref liveOutputRevision, 0);
            errorCount = 0;
            warningCount = 0;
            lastErrorPreview = null;

            var buildBanner = WriteBuildStartBanner(buildReason);
            SetState(ProjectLifecycleState.Building);

            buildProgressTracker = new BuildProgressTracker();
            buildProgressTracker.Reset();
            progressSteps = buildProgressTracker.Steps;
            NotifyProgressChanged(force: true);

            var releaseLocks = definition.RunOptions.ReleaseOutputLocksBeforeBuild;
            if (releaseLocks)
            {
                await ReleaseOutputLocksAsync(cancellationToken);
            }

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
            errorCount = buildLog.ErrorCount;
            warningCount = BuildLogParser.ParseWarningCount(result.Output);
            lastErrorPreview = buildLog.ErrorLines.FirstOrDefault();

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

            if (shouldRestartRun
                && definition.RunOptions.RestartAppAfterRebuild
                && definition.RunOptions.RunMode != ProjectRunMode.None
                && result.ExitCode == 0)
            {
                if (triggeredByFileChange)
                {
                    await Task.Delay(1500, cancellationToken);
                }

                StartRunProcess(skipEmbeddedBuild: true);
            }
        }
        finally
        {
            Interlocked.Exchange(ref buildInProgress, 0);
            Interlocked.Exchange(ref buildTriggeredByFileChange, 0);
            fileWatcher?.Resume();

            if (triggeredByFileChange)
            {
                fileChangeBuildCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(3);
            }

            if (pendingFileChangeRebuild)
            {
                pendingFileChangeRebuild = false;
                _ = ScheduleCoalescedFileChangeRebuildAsync();
            }
        }
    }

    private async Task ScheduleCoalescedFileChangeRebuildAsync()
    {
        var delay = fileChangeBuildCooldownUntil - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay);
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            pendingFileChangeRebuild = true;
            return;
        }

        if (DateTimeOffset.UtcNow < fileChangeBuildCooldownUntil)
        {
            pendingFileChangeRebuild = true;
            _ = ScheduleCoalescedFileChangeRebuildAsync();
            return;
        }

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
        errorCount = metadata.ErrorCount;
        lastErrorPreview = metadata.ErrorLines.FirstOrDefault();
        if (File.Exists(metadata.LogFilePath))
        {
            var logText = await File.ReadAllTextAsync(metadata.LogFilePath, cancellationToken);
            warningCount = BuildLogParser.ParseWarningCount(logText);
        }

        RefreshHealth();
        HealthChanged?.Invoke();
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

    private void OnFileWatcherChanged()
    {
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

        if (DotNetRunOutputParser.TryExtractListeningUrl(line, out var parsedUrl))
        {
            pendingListenUrl = parsedUrl;
            MarkListenUrlReady(parsedUrl);
        }

        if (DotNetRunOutputParser.IsHostTerminatedLine(line)
            || DotNetRunOutputParser.IsFatalStartupLine(line))
        {
            lastErrorPreview = line.Trim();
            errorCount = Math.Max(errorCount, 1);
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

        if (definition.RunOptions.RunMode == ProjectRunMode.Watch)
        {
            HandleWatchProcessOutputLine(line);
        }
    }

    private void HandleWatchProcessOutputLine(string line)
    {
        if (DotNetWatchOutput.IsBuildFailedLine(line))
        {
            watchRebuildInProgress = false;
            lastBuildExitCode = 1;
            lastErrorPreview = line.Trim();
            errorCount = Math.Max(errorCount, 1);
            SetState(ProjectLifecycleState.BuildFailed);
            return;
        }

        if (DotNetWatchOutput.IsBuildSucceededLine(line))
        {
            watchRebuildInProgress = false;
            lastBuildExitCode = 0;
            lastErrorPreview = null;
            errorCount = 0;
            if (state is ProjectLifecycleState.BuildFailed)
            {
                SetState(ProjectLifecycleState.Watching);
            }

            return;
        }

        if (!DotNetWatchOutput.IsFileChangeLine(line))
        {
            return;
        }

        if (Volatile.Read(ref testInProgress) != 0
            || DateTimeOffset.UtcNow < fileChangeBuildCooldownUntil)
        {
            return;
        }

        watchRebuildInProgress = true;
        listenUrlReady = false;
        listenUrlNotified = false;

        var now = DateTimeOffset.UtcNow;
        if ((now - lastWatchFileChangeNotifyUtc).TotalSeconds < 2)
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

    public async Task RestartAppAsync(CancellationToken cancellationToken)
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

        var needsRebuild = lastBuildExitCode != 0;
        await StopRunProcessAsync(cancellationToken);
        restartCount = 0;

        if (needsRebuild)
        {
            PrepareBuild("app restart");
            await BuildAsync(cancellationToken);
        }

        EnsureRunProcessStartedAfterBuild();
        HealthChanged?.Invoke();
    }

    private void OnBuildOutputLine(string line)
    {
        lock (liveOutputSync)
        {
            liveBuildOutput.AppendLine(line);
        }

        Interlocked.Increment(ref liveOutputRevision);

        var progressChanged = false;
        if (buildProgressTracker is not null && buildProgressTracker.OnOutputLine(line))
        {
            progressSteps = buildProgressTracker.Steps;
            progressChanged = true;
        }

        var countsChanged = RefreshLiveIssueCounts(force: false);
        if (countsChanged)
        {
            RefreshHealth();
        }

        if (countsChanged || progressChanged)
        {
            NotifyProgressChanged();
        }
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
        if (parsedErrors == errorCount && parsedWarnings == warningCount)
        {
            return false;
        }

        errorCount = parsedErrors;
        warningCount = parsedWarnings;
        return true;
    }

    private void NotifyProgressChanged(bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && (now - lastProgressPublishUtc).TotalMilliseconds < 150)
        {
            return;
        }

        lastProgressPublishUtc = now;
        HealthChanged?.Invoke();
    }

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
            var buildErrorCount = errorCount;
            var buildWarningCount = warningCount;
            errorCount = 0;
            warningCount = 0;
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
                errorCount = 1;
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
                errorCount = buildErrorCount;
                warningCount = buildWarningCount;
                SetState(ProjectLifecycleState.TestOk);
            }
            else
            {
                errorCount = Math.Max(parsed.ErrorCount, testsExecuted ? 0 : 1);
                warningCount = BuildLogParser.ParseWarningCount(logText);
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

            HealthChanged?.Invoke();
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

        var countsChanged = RefreshLiveIssueCounts(force: false);
        if (countsChanged)
        {
            RefreshHealth();
            NotifyProgressChanged();
        }
    }

    private void StartRunProcess(bool skipEmbeddedBuild = false)
    {
        StopRunProcess();

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

        var args = definition.RunOptions.RunMode == ProjectRunMode.Watch
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

                if (definition.RunOptions.RunMode == ProjectRunMode.Watch
                    && !definition.RunOptions.AutoRestartOnWatchChanges)
                {
                    psi.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "0";
                }
            });

        NotifyProgressChanged(force: true);

        SetState(definition.RunOptions.RunMode == ProjectRunMode.Watch
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
        RefreshHealth();
        HealthChanged?.Invoke();
    }

    private void RefreshHealth() =>
        health = ProjectHealthEvaluator.Evaluate(state, lastBuildExitCode, errorCount, warningCount);

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

    public void Dispose()
    {
        StopListenUrlPolling();
        StopRunLogSaveTimer();
        fileWatcher?.Dispose();
        runProcess?.Dispose();
    }
}
