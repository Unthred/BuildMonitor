using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

public sealed partial class ProjectOrchestrator : IDisposable
{
    private readonly DotNetCliRunner cliRunner = new();
    private readonly BuildLogStore logStore;
    private readonly BuildTriggerJournal triggerJournal;
    private readonly ControlPlaneEventJournal controlPlaneEventJournal;
    private readonly FileChangeBurstStatsStore burstStatsStore;
    private readonly BuildTrainingStore trainingStore;
    private readonly ControlPlaneSessionStore sessionStore;
    private readonly ControlPlaneMetricsStore metricsStore;
    private readonly Dictionary<string, ProjectRuntime> runtimes = new();
    private readonly object sync = new();
    private readonly HealthCoalescer healthCoalescer;
    private AppSettings settings = new();
    private Action<AppSettings>? settingsPersistRequested;

    public event Action<IReadOnlyList<ProjectHealthSnapshot>, MonitorHealth>? HealthUpdated;
    public event Action<string, string, string, UserNotificationKind, UserNotificationCategory>? UserNotification;

    /// <summary>Optional disk persist hook (tray wires SettingsStore.SaveAsync).</summary>
    public void SetSettingsPersistHandler(Action<AppSettings>? handler) =>
        settingsPersistRequested = handler;

    public ProjectOrchestrator(string logsRootDirectory, string? appDataDirectory = null)
    {
        logStore = new BuildLogStore(logsRootDirectory);
        var dataRoot = appDataDirectory
            ?? Path.GetDirectoryName(logsRootDirectory)
            ?? logsRootDirectory;
        triggerJournal = new BuildTriggerJournal(dataRoot);
        controlPlaneEventJournal = new ControlPlaneEventJournal(dataRoot);
        burstStatsStore = new FileChangeBurstStatsStore(dataRoot);
        trainingStore = new BuildTrainingStore(dataRoot);
        metricsStore = new ControlPlaneMetricsStore(controlPlaneEventJournal);
        sessionStore = new ControlPlaneSessionStore(metricsStore, controlPlaneEventJournal);
        WorkerHealthRegistry.Shared.Register(
            "health.event.raise",
            "HealthUpdated event (background → UI)",
            TimeSpan.FromMilliseconds(750),
            "Background");
        healthCoalescer = new HealthCoalescer(GetCoalescerState, PublishHealthFromCoalescer);
    }

    public ControlPlaneSessionStore SessionStore => sessionStore;

    public ControlPlaneMetricsStore MetricsStore => metricsStore;

    public ControlPlaneEventJournal ControlPlaneEventJournal => controlPlaneEventJournal;

    public BuildTriggerJournal TriggerJournal => triggerJournal;

    public void SetTrayMenuOpen(bool open) => healthCoalescer.SetTrayMenuOpen(open);

    private (IReadOnlyList<ProjectRuntime> Runtimes, IReadOnlyList<LocalProjectDefinition> Inactive) GetCoalescerState()
    {
        lock (sync)
        {
            var inactive = settings.Projects
                .Where(p => !p.IsActiveInSession)
                .ToList();
            return (runtimes.Values.ToList(), inactive);
        }
    }

    private void PublishHealthFromCoalescer(IReadOnlyList<ProjectHealthSnapshot> snapshots, MonitorHealth rollup)
    {
        var registry = WorkerHealthRegistry.Shared;
        registry.Heartbeat(
            "health.event.raise",
            note: $"{snapshots.Count} snapshots",
            managedThreadId: Environment.CurrentManagedThreadId);
        HealthUpdated?.Invoke(snapshots, rollup);
    }

    private void OnRuntimeHealthCoalesceRequested(bool immediate) =>
        healthCoalescer.Request(immediate);

    public void NotifyControlPlaneSessionChanged(string projectId, bool immediate = true)
    {
        ProjectRuntime? runtime;
        lock (sync)
        {
            runtimes.TryGetValue(projectId, out runtime);
        }

        runtime?.NotifyControlPlaneChanged(immediate);
        healthCoalescer.Request(immediate);
    }

    public BuildLogStore LogStore => logStore;

    public BuildVerdictTrainingResult ProcessUnexpectedVerdict(BuildTriggerRecord record)
    {
        lock (sync)
        {
            var project = settings.Projects.FirstOrDefault(p => p.Id == record.ProjectId);
            var configured = project?.RunOptions.WatchExcludeSegments;
            var learned = trainingStore.GetLearnedExcludeSegments(record.ProjectId);
            var learn = settings.Monitor.LearnFromDiagnosticsVerdicts;

            var result = BuildVerdictTrainer.ProcessUnexpectedVerdict(
                record,
                configured,
                learned,
                learn,
                trainingStore.RecordUnexpectedVerdict,
                projectId => _ = burstStatsStore.RecordUnexpectedVerdict(projectId));

            if (result.AppliedDebounceFeedback && runtimes.TryGetValue(record.ProjectId, out var runtime))
            {
                runtime.RefreshFileWatcherDebounce();
            }

            return result;
        }
    }

    public IReadOnlyList<string> ApplyLearnedExcludeSegments(string projectId, IReadOnlyList<string> segments)
    {
        lock (sync)
        {
            var added = trainingStore.AddLearnedExcludeSegments(projectId, segments);
            if (runtimes.TryGetValue(projectId, out var runtime))
            {
                runtime.RefreshWatchIgnoreSegments(segments);
            }

            return added;
        }
    }

    public IReadOnlyList<BuildIntelligenceSnapshot> GetBuildIntelligenceSnapshots()
    {
        lock (sync)
        {
            var monitor = settings.Monitor;
            var activeProjects = settings.Projects
                .Where(p => p.IsActiveInSession)
                .ToList();
            var activeIds = new HashSet<string>(
                activeProjects.Select(p => p.Id),
                StringComparer.OrdinalIgnoreCase);
            var triggerCounts = triggerJournal.GetEntries()
                .GroupBy(e => e.ProjectId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var snapshots = new List<BuildIntelligenceSnapshot>();

            foreach (var runtime in runtimes.Values.Where(r => activeIds.Contains(r.ProjectId)))
            {
                var count = triggerCounts.GetValueOrDefault(runtime.ProjectId);
                snapshots.Add(runtime.GetIntelligenceSnapshot(monitor, count));
            }

            foreach (var project in activeProjects.Where(p => !runtimes.ContainsKey(p.Id)))
            {
                var count = triggerCounts.GetValueOrDefault(project.Id);
                snapshots.Add(BuildIntelligenceSnapshot.FromStoredStats(
                    project,
                    monitor,
                    burstStatsStore.GetOrDefault(project.Id)) with
                {
                    TodayTriggerCount = count
                });
            }

            return snapshots
                .OrderBy(s => s.ProjectDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

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
            sessionStore.ApplyMonitorDefaults(
                newSettings.Monitor.ControlPlaneBusyTimeoutSeconds,
                newSettings.Monitor.SuppressAutoBuildTests);

            var activeIds = newSettings.Projects
                .Where(p => p.IsActiveInSession)
                .Select(p => p.Id)
                .ToHashSet();

            idsToStop = runtimes.Keys.Where(id => !activeIds.Contains(id)).ToList();

            foreach (var project in newSettings.Projects.Where(p => p.IsActiveInSession))
            {
                if (!runtimes.ContainsKey(project.Id))
                {
                    var runtime = new ProjectRuntime(
                        project,
                        logStore,
                        cliRunner,
                        triggerJournal,
                        burstStatsStore,
                        trainingStore,
                        RaiseUserNotification);
                    runtime.SetSessionStore(sessionStore);
                    runtime.SetMetricsStore(metricsStore);
                    runtime.HealthCoalesceRequested += OnRuntimeHealthCoalesceRequested;
                    runtimes[project.Id] = runtime;
                }

                runtimes[project.Id].SetSessionStore(sessionStore);
                runtimes[project.Id].SetMetricsStore(metricsStore);
                runtimes[project.Id].UpdateDefinition(project, newSettings.Monitor);
                runtimes[project.Id].SetUserNotifier(RaiseUserNotification);
            }
        }

        foreach (var id in idsToStop)
        {
            StopProject(id);
        }

        healthCoalescer.Request(immediate: true);
    }

    public async Task StartActiveProjectsAsync(CancellationToken cancellationToken)
    {
        List<ProjectRuntime> toStart;
        lock (sync)
        {
            toStart = runtimes.Values
                .Where(runtime => ShouldStartOnLaunch(runtime.ProjectId))
                .Take(settings.Monitor.MaxConcurrentActiveProjects)
                .ToList();
        }

        foreach (var runtime in toStart)
        {
            try
            {
                await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
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

    private bool ShouldStartOnLaunch(string projectId)
    {
        var project = settings.Projects.FirstOrDefault(p => p.Id == projectId);
        return project is { IsActiveInSession: true, StartOnLaunch: true };
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

            healthCoalescer.Request(immediate: true);
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
            healthCoalescer.Request(immediate: true);
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

    public StillEditingClickResult HandleStillEditingClick(string projectId)
    {
        lock (sync)
        {
            return runtimes.TryGetValue(projectId, out var runtime)
                ? runtime.HandleStillEditingClick()
                : StillEditingClickResult.NotApplicable;
        }
    }

    public async Task RestartAppAsync(string projectId, CancellationToken cancellationToken) =>
        await RestartProjectAsync(projectId, rebuildFirst: false, cancellationToken);

    public async Task RebuildAndRestartAsync(string projectId, CancellationToken cancellationToken) =>
        await RestartProjectAsync(projectId, rebuildFirst: true, cancellationToken);

    private async Task RestartProjectAsync(
        string projectId,
        bool rebuildFirst,
        CancellationToken cancellationToken)
    {
        if (!runtimes.TryGetValue(projectId, out var runtime))
        {
            return;
        }

        try
        {
            if (rebuildFirst)
            {
                await runtime.RebuildAndRestartAsync(cancellationToken);
            }
            else
            {
                await runtime.RestartAppAsync(cancellationToken);
            }

            healthCoalescer.Request(immediate: true);
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
        healthCoalescer.Request(immediate: true);
    }

    public async Task RepairBuildOutputAsync(string projectId, CancellationToken cancellationToken)
    {
        if (!runtimes.TryGetValue(projectId, out var runtime))
        {
            return;
        }

        try
        {
            var wasRunning = runtime.IsRunProcessActive;
            var repair = await runtime.RepairBuildOutputAsync(cancellationToken, restartAfter: wasRunning);
            healthCoalescer.Request(immediate: true);

            if (repair.Repaired)
            {
                RaiseUserNotification(
                    runtime.ProjectId,
                    $"Cleaned build output — {runtime.DisplayName}",
                    $"Removed {string.Join(", ", repair.RemovedFolders)}"
                    + (wasRunning ? Environment.NewLine + "Restarted watch/run." : string.Empty),
                    UserNotificationKind.Info,
                    UserNotificationCategory.Info);
                return;
            }

            if (repair.Failures.Count > 0)
            {
                RaiseUserNotification(
                    runtime.ProjectId,
                    $"Clean build output failed — {runtime.DisplayName}",
                    string.Join(Environment.NewLine, repair.Failures.Take(4)),
                    UserNotificationKind.Error,
                    UserNotificationCategory.Error);
            }
            else
            {
                RaiseUserNotification(
                    runtime.ProjectId,
                    $"Nothing to clean — {runtime.DisplayName}",
                    "No artifacts/, bin/, or obj/ folders were found.",
                    UserNotificationKind.Info,
                    UserNotificationCategory.Info);
            }
        }
        catch (Exception ex)
        {
            RaiseUserNotification(
                runtime.ProjectId,
                $"Clean build output failed — {runtime.DisplayName}",
                ExceptionDetailFormatter.Format(ex),
                UserNotificationKind.Error,
                UserNotificationCategory.Error);
        }
    }

    public void StopProject(string projectId)
    {
        // Synchronous wrapper for callers that cannot await; never call while holding sync.
        StopProjectAsync(projectId).GetAwaiter().GetResult();
    }

    public IReadOnlyList<ProjectHealthSnapshot> GetHealthSnapshots() =>
        healthCoalescer.GetSnapshots();

    private void RaiseUserNotification(
        string projectId,
        string title,
        string message,
        UserNotificationKind kind,
        UserNotificationCategory category) =>
        UserNotification?.Invoke(projectId, title, message, kind, category);

    public void Dispose()
    {
        healthCoalescer.Dispose();
        lock (sync)
        {
            foreach (var runtime in runtimes.Values)
            {
                runtime.Dispose();
            }

            runtimes.Clear();
        }
    }
}
