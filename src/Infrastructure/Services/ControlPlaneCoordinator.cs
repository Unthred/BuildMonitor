using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Infrastructure.Services;

/// <summary>HTTP control-plane actions backed by the project orchestrator.</summary>
public sealed class ControlPlaneCoordinator : IControlPlaneActions
{
    private readonly ProjectOrchestrator orchestrator;
    private readonly ControlPlaneSessionStore sessions;
    private readonly ControlPlaneEventJournal events;

    public ControlPlaneCoordinator(
        ProjectOrchestrator orchestrator,
        ControlPlaneSessionStore sessions,
        ControlPlaneEventJournal events)
    {
        this.orchestrator = orchestrator;
        this.sessions = sessions;
        this.events = events;
    }

    public IReadOnlyList<ControlPlaneProjectInfo> ListProjects() =>
        orchestrator.ListControlPlaneProjects();

    public bool ProjectExists(string projectId) =>
        orchestrator.ControlPlaneProjectExists(projectId);

    public ControlPlaneSessionStatus GetSession(string projectId) =>
        sessions.GetStatus(projectId);

    public ControlPlaneSessionStatus MarkBusy(string projectId, bool? suppressAutoBuildTests)
    {
        var status = sessions.MarkBusy(projectId, suppressAutoBuildTests);
        orchestrator.NotifyControlPlaneSessionChanged(projectId);
        return status;
    }

    public ControlPlaneSessionStatus MarkIdle(string projectId, bool? suppressAutoBuildTests)
    {
        var status = sessions.MarkIdle(projectId, suppressAutoBuildTests);
        orchestrator.NotifyControlPlaneSessionChanged(projectId);
        return status;
    }

    public ControlPlaneWatchStatus GetWatch(string projectId) =>
        orchestrator.GetControlPlaneWatch(projectId);

    public ControlPlaneWatchStatus PauseWatch(string projectId)
    {
        var watch = orchestrator.PauseControlPlaneWatchAsync(projectId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        events.Record(projectId, ControlPlaneEventKind.WatchPause, "Watch paused");
        return watch;
    }

    public ControlPlaneWatchStatus ResumeWatch(string projectId)
    {
        var watch = orchestrator.ResumeControlPlaneWatch(projectId);
        events.Record(projectId, ControlPlaneEventKind.WatchResume, "Watch resumed");
        return watch;
    }

    public async Task<ControlPlaneRebuildResult> RebuildAsync(
        ControlPlaneRebuildRequest request,
        CancellationToken cancellationToken)
    {
        sessions.MarkIdle(request.ProjectId);
        orchestrator.NotifyControlPlaneSessionChanged(request.ProjectId);
        var result = await orchestrator.RunControlPlaneRebuildAsync(request, cancellationToken)
            .ConfigureAwait(false);
        events.Record(
            request.ProjectId,
            ControlPlaneEventKind.Rebuild,
            result.Ok ? "Agent rebuild passed" : "Agent rebuild failed",
            $"exit {result.ExitCode}");
        return result;
    }

    public async Task<ControlPlaneRunTestsResult> RunTestsAsync(
        ControlPlaneRunTestsRequest request,
        CancellationToken cancellationToken)
    {
        sessions.MarkIdle(request.ProjectId);
        orchestrator.NotifyControlPlaneSessionChanged(request.ProjectId);
        var result = await orchestrator.RunControlPlaneTestsAsync(request, cancellationToken)
            .ConfigureAwait(false);
        events.Record(
            request.ProjectId,
            ControlPlaneEventKind.Tests,
            result.Ok ? "Agent tests passed" : "Agent tests failed");
        return result;
    }

    public async Task<ControlPlaneRunStopResult> StopRunAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.StopControlPlaneRunAsync(projectId, cancellationToken)
            .ConfigureAwait(false);
        events.Record(
            projectId,
            ControlPlaneEventKind.RunStop,
            result.WasRunning ? "App stopped" : "App already stopped",
            result.ExitCode is null ? null : $"exit {result.ExitCode}");
        return result;
    }

    public async Task<ControlPlaneShipCheckResult> ShipCheckAsync(
        ControlPlaneShipCheckRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SuppressAutoBuildTests.HasValue)
        {
            sessions.SetSuppressAutoBuildTests(request.ProjectId, request.SuppressAutoBuildTests.Value);
        }

        var result = await orchestrator.RunControlPlaneShipCheckAsync(request, cancellationToken)
            .ConfigureAwait(false);
        events.Record(
            request.ProjectId,
            ControlPlaneEventKind.ShipCheck,
            result.Ok ? "Ship-check passed" : "Ship-check failed",
            result.Build);
        return result;
    }

    public ControlPlaneModeStatus GetBuildControlMode(string projectId) =>
        orchestrator.GetControlPlaneBuildControlMode(projectId);

    public ControlPlaneModeStatus SetBuildControlMode(string projectId, ProjectBuildControlMode mode)
    {
        var status = orchestrator.SetControlPlaneBuildControlMode(projectId, mode);
        events.Record(
            projectId,
            ControlPlaneEventKind.ModeChanged,
            $"Build control → {status.ModeWire}",
            status.PreviousModeWire is null
                ? null
                : $"was {status.PreviousModeWire}");
        return status;
    }
}
