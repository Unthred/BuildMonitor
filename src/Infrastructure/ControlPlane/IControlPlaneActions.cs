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
    Task<ControlPlaneShipCheckResult> ShipCheckAsync(
        ControlPlaneShipCheckRequest request,
        CancellationToken cancellationToken);
    bool ProjectExists(string projectId);
}
