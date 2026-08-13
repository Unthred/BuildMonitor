namespace BuildMonitor.Core.Models;

public enum ControlPlaneSessionState
{
    Idle = 0,
    Busy = 1
}

public enum ControlPlaneWatchState
{
    Stopped = 0,
    Running = 1,
    Paused = 2
}

public sealed record ControlPlaneProjectInfo(
    string Id,
    string DisplayName,
    string RootFolder,
    string ProjectFile,
    bool IsActiveInSession);

public sealed record ControlPlaneSessionStatus(
    ControlPlaneSessionState State,
    DateTimeOffset Since,
    bool SessionApiUsed,
    bool SuppressAutoBuildTests);

public sealed record ControlPlaneWatchStatus(
    ControlPlaneWatchState Watch,
    int? Pid);

public sealed record ControlPlaneShipCheckRequest(
    string ProjectId,
    string? Configuration,
    string? Filter,
    bool? SuppressAutoBuildTests);

public sealed record ControlPlaneTestCounts(int Failed, int Passed, int Skipped);

public sealed record ControlPlaneShipCheckResult(
    bool Ok,
    string Project,
    string Build,
    ControlPlaneTestCounts? Tests,
    IReadOnlyList<string> Failures,
    string? Log);
