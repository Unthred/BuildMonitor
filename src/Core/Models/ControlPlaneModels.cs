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

public enum ControlPlaneShipCheckPhase
{
    None = 0,
    Preparing = 1,
    Building = 2,
    Testing = 3,
    ResumingWatch = 4
}

public enum ControlPlaneShipCheckOutcome
{
    None = 0,
    Passed = 1,
    Failed = 2
}

/// <summary>User-meaningful control-plane state embedded in project health snapshots.</summary>
public sealed record ProjectControlPlaneSnapshot(
    bool SessionApiUsed,
    ControlPlaneSessionState EffectiveSessionState,
    DateTimeOffset? SessionSinceUtc,
    bool AutoBuildBlockedBySession,
    bool HasPendingFileChangeRebuild,
    int PendingFileChangeCount,
    ControlPlaneShipCheckPhase ShipCheckPhase,
    ControlPlaneShipCheckOutcome LastShipCheckOutcome,
    DateTimeOffset? LastShipCheckCompletedUtc,
    bool ShipCheckInProgress)
{
    public static ProjectControlPlaneSnapshot Unused { get; } = new(
        SessionApiUsed: false,
        EffectiveSessionState: ControlPlaneSessionState.Idle,
        SessionSinceUtc: null,
        AutoBuildBlockedBySession: false,
        HasPendingFileChangeRebuild: false,
        PendingFileChangeCount: 0,
        ShipCheckPhase: ControlPlaneShipCheckPhase.None,
        LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
        LastShipCheckCompletedUtc: null,
        ShipCheckInProgress: false);
}
