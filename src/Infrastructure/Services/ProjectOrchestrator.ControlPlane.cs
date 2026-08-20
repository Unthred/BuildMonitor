using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Infrastructure.Services;

public sealed partial class ProjectOrchestrator
{
    public IReadOnlyList<ControlPlaneProjectInfo> ListControlPlaneProjects()
    {
        lock (sync)
        {
            return settings.Projects
                .Select(p => new ControlPlaneProjectInfo(
                    p.Id,
                    p.DisplayName,
                    p.RootFolder,
                    p.ProjectFile,
                    p.IsActiveInSession))
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
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
