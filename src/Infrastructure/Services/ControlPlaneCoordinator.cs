using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Infrastructure.Services;

/// <summary>HTTP control-plane actions backed by the project orchestrator.</summary>
public sealed class ControlPlaneCoordinator : IControlPlaneActions
{
    private readonly ProjectOrchestrator orchestrator;
    private readonly ControlPlaneSessionStore sessions;

    public ControlPlaneCoordinator(ProjectOrchestrator orchestrator, ControlPlaneSessionStore sessions)
    {
        this.orchestrator = orchestrator;
        this.sessions = sessions;
    }

    public IReadOnlyList<ControlPlaneProjectInfo> ListProjects() =>
        orchestrator.ListControlPlaneProjects();

    public bool ProjectExists(string projectId) =>
        orchestrator.ControlPlaneProjectExists(projectId);

    public ControlPlaneSessionStatus GetSession(string projectId) =>
        sessions.GetStatus(projectId);

    public ControlPlaneSessionStatus MarkBusy(string projectId, bool? suppressAutoBuildTests) =>
        sessions.MarkBusy(projectId, suppressAutoBuildTests);

    public ControlPlaneSessionStatus MarkIdle(string projectId, bool? suppressAutoBuildTests) =>
        sessions.MarkIdle(projectId, suppressAutoBuildTests);

    public ControlPlaneWatchStatus GetWatch(string projectId) =>
        orchestrator.GetControlPlaneWatch(projectId);

    public ControlPlaneWatchStatus PauseWatch(string projectId) =>
        orchestrator.PauseControlPlaneWatchAsync(projectId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    public ControlPlaneWatchStatus ResumeWatch(string projectId) =>
        orchestrator.ResumeControlPlaneWatch(projectId);

    public Task<ControlPlaneShipCheckResult> ShipCheckAsync(
        ControlPlaneShipCheckRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SuppressAutoBuildTests.HasValue)
        {
            sessions.SetSuppressAutoBuildTests(request.ProjectId, request.SuppressAutoBuildTests.Value);
        }

        return orchestrator.RunControlPlaneShipCheckAsync(request, cancellationToken);
    }
}
