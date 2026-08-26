using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Infrastructure.Services;

public sealed partial class ProjectOrchestrator
{
    public IReadOnlyList<ControlPlaneProjectInfo> ListControlPlaneProjects()
    {
        // Copy settings under lock, then map from published/cached snapshots outside the
        // orchestrator lock so Azure poll / health coalesce are not blocked.
        List<(
            string Id,
            string DisplayName,
            string RootFolder,
            string ProjectFile,
            bool IsActiveInSession,
            bool HasLocal,
            bool AzureAttached)> projects;
        lock (sync)
        {
            projects = settings.Projects
                .Select(p => (
                    p.Id,
                    p.DisplayName,
                    p.Local?.RootFolder ?? string.Empty,
                    p.Local?.ProjectFile ?? string.Empty,
                    p.IsActiveInSession,
                    p.Local is not null,
                    p.Azure is not null))
                .ToList();
        }

        var utcNow = DateTimeOffset.UtcNow;
        return projects
            .Select(p =>
            {
                var snapshot = healthCoalescer.TryGetControlPlaneSnapshot(p.Id);
                var session = sessionStore.GetStatus(p.Id);
                return ControlPlaneProjectStatusMapper.Map(
                    p.Id,
                    p.DisplayName,
                    p.RootFolder,
                    p.ProjectFile,
                    p.IsActiveInSession,
                    p.HasLocal,
                    p.AzureAttached,
                    snapshot,
                    session,
                    utcNow);
            })
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool ControlPlaneProjectExists(string projectId)
    {
        lock (sync)
        {
            return settings.Projects.Any(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public ControlPlaneWatchStatus GetControlPlaneWatch(string projectId)
    {
        lock (sync)
        {
            if (!runtimes.TryGetValue(projectId, out var runtime))
            {
                return new ControlPlaneWatchStatus(ControlPlaneWatchState.Stopped, Pid: null);
            }

            return runtime.GetWatchStatus();
        }
    }

    public async Task<ControlPlaneWatchStatus> PauseControlPlaneWatchAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ProjectRuntime? runtime;
        lock (sync)
        {
            runtimes.TryGetValue(projectId, out runtime);
        }

        if (runtime is null)
        {
            return new ControlPlaneWatchStatus(ControlPlaneWatchState.Stopped, Pid: null);
        }

        return await runtime.PauseWatchAsync(cancellationToken).ConfigureAwait(false);
    }

    public ControlPlaneWatchStatus ResumeControlPlaneWatch(string projectId)
    {
        lock (sync)
        {
            if (!runtimes.TryGetValue(projectId, out var runtime))
            {
                return new ControlPlaneWatchStatus(ControlPlaneWatchState.Stopped, Pid: null);
            }

            return runtime.ResumeWatch();
        }
    }

    public ControlPlaneMetricsSnapshot GetControlPlaneMetrics(string projectId) =>
        metricsStore.GetSnapshot(projectId, sessionStore.GetStatus(projectId));

    public ControlPlaneWorkflowSnapshot GetControlPlaneWorkflow(string projectId)
    {
        var metrics = GetControlPlaneMetrics(projectId);
        return ControlPlaneWorkflowAnalyzer.Analyze(
            projectId,
            sessionStore.GetStatus(projectId),
            controlPlaneEventJournal.GetEntries(),
            triggerJournal.GetEntries(),
            metrics.AutoBuildsBlocked,
            DateTimeOffset.UtcNow);
    }

    public async Task<ControlPlaneShipCheckResult> RunControlPlaneShipCheckAsync(
        ControlPlaneShipCheckRequest request,
        CancellationToken cancellationToken)
    {
        var runtime = EnsureControlPlaneRuntime(request.ProjectId);

        var started = DateTimeOffset.UtcNow;
        try
        {
            var result = await runtime.RunShipCheckAsync(
                request.Configuration,
                request.Filter,
                cancellationToken).ConfigureAwait(false);
            metricsStore.RecordShipCheck(request.ProjectId, result.Ok, DateTimeOffset.UtcNow - started);
            return result;
        }
        catch
        {
            metricsStore.RecordShipCheck(request.ProjectId, ok: false, DateTimeOffset.UtcNow - started);
            throw;
        }
    }

    public async Task<ControlPlaneRebuildResult> RunControlPlaneRebuildAsync(
        ControlPlaneRebuildRequest request,
        CancellationToken cancellationToken)
    {
        var runtime = EnsureControlPlaneRuntime(request.ProjectId);

        try
        {
            return await runtime.RunAgentRebuildAsync(request.Configuration, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            healthCoalescer.Request(immediate: true);
        }
    }

    public async Task<ControlPlaneRunStopResult> StopControlPlaneRunAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ProjectRuntime? runtime;
        lock (sync)
        {
            runtimes.TryGetValue(projectId, out runtime);
        }

        if (runtime is null)
        {
            return new ControlPlaneRunStopResult(
                Ok: true,
                WasRunning: false,
                ExitCode: null,
                Watch: new ControlPlaneWatchStatus(ControlPlaneWatchState.Stopped, Pid: null));
        }

        try
        {
            return await runtime.StopRunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            healthCoalescer.Request(immediate: true);
        }
    }

    public async Task<ControlPlaneRunTestsResult> RunControlPlaneTestsAsync(
        ControlPlaneRunTestsRequest request,
        CancellationToken cancellationToken)
    {
        var runtime = EnsureControlPlaneRuntime(request.ProjectId);

        try
        {
            return await runtime.RunAgentTestsAsync(
                    request.Configuration,
                    request.Filter,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            healthCoalescer.Request(immediate: true);
        }
    }

    public ControlPlaneModeStatus GetControlPlaneBuildControlMode(string projectId)
    {
        lock (sync)
        {
            var project = settings.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                throw new InvalidOperationException($"Unknown projectId '{projectId}'.");
            }

            var mode = project.Local?.BuildControlMode ?? ProjectBuildControlMode.FileWatching;
            if (runtimes.TryGetValue(project.Id, out var runtime))
            {
                mode = runtime.GetBuildControlMode();
            }

            return new ControlPlaneModeStatus(
                project.Id,
                mode,
                ProjectBuildControlModeWire.ToWire(mode));
        }
    }

    public ControlPlaneModeStatus SetControlPlaneBuildControlMode(
        string projectId,
        ProjectBuildControlMode mode)
    {
        ControlPlaneModeStatus status;
        AppSettings snapshot;
        lock (sync)
        {
            var project = settings.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                throw new InvalidOperationException($"Unknown projectId '{projectId}'.");
            }

            if (project.Local is null)
            {
                throw new InvalidOperationException(
                    $"Project '{projectId}' has no Local attachment; build-control mode does not apply.");
            }

            if (runtimes.TryGetValue(project.Id, out var runtime))
            {
                status = runtime.SetBuildControlMode(mode);
            }
            else
            {
                var previous = project.Local.BuildControlMode;
                project.Local.BuildControlMode = mode;
                status = new ControlPlaneModeStatus(
                    project.Id,
                    mode,
                    ProjectBuildControlModeWire.ToWire(mode),
                    previous,
                    ProjectBuildControlModeWire.ToWire(previous));
            }

            // Keep settings list in sync when runtime holds the same definition reference.
            project.Local.BuildControlMode = mode;
            snapshot = settings;
        }

        settingsPersistRequested?.Invoke(snapshot);
        healthCoalescer.Request(immediate: true);
        return status;
    }

    private ProjectRuntime EnsureControlPlaneRuntime(string projectId)
    {
        ProjectRuntime? runtime;
        lock (sync)
        {
            runtimes.TryGetValue(projectId, out runtime);
        }

        if (runtime is not null)
        {
            return runtime;
        }

        lock (sync)
        {
            var project = settings.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                throw new InvalidOperationException($"Unknown projectId '{projectId}'.");
            }

            if (!runtimes.TryGetValue(project.Id, out runtime))
            {
                runtime = new ProjectRuntime(
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
                runtime.UpdateDefinition(project, settings.Monitor);
                runtimes[project.Id] = runtime;
            }

            return runtime;
        }
    }
}
