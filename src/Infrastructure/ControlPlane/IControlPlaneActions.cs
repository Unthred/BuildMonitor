using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.ControlPlane;

public interface IControlPlaneActions
{
    IReadOnlyList<ControlPlaneProjectInfo> ListProjects();
    ControlPlaneSessionStatus GetSession(string projectId);
    ControlPlaneSessionStatus MarkBusy(string projectId, bool? suppressAutoBuildTests);
    ControlPlaneSessionStatus MarkIdle(string projectId, bool? suppressAutoBuildTests);
    ControlPlaneWatchStatus GetWatch(string projectId);
    ControlPlaneWatchStatus PauseWatch(string projectId);
    ControlPlaneWatchStatus ResumeWatch(string projectId);
    Task<ControlPlaneRebuildResult> RebuildAsync(
        ControlPlaneRebuildRequest request,
        CancellationToken cancellationToken);
    Task<ControlPlaneRunTestsResult> RunTestsAsync(
        ControlPlaneRunTestsRequest request,
        CancellationToken cancellationToken);
    Task<ControlPlaneRunStopResult> StopRunAsync(
        string projectId,
        CancellationToken cancellationToken);
    Task<ControlPlaneShipCheckResult> ShipCheckAsync(
        ControlPlaneShipCheckRequest request,
        CancellationToken cancellationToken);
    ControlPlaneModeStatus GetBuildControlMode(string projectId);
    ControlPlaneModeStatus SetBuildControlMode(string projectId, ProjectBuildControlMode mode);
    bool ProjectExists(string projectId);
}
