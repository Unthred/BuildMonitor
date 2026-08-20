namespace BuildMonitor.Core.Models;

public enum ControlPlaneSessionState
{
    Idle = 0,
    Busy = 1
}

/// <summary>Why the session is currently idle after the API has been used.</summary>
public enum ControlPlaneIdleCause
{
    None = 0,
    Agent = 1,
    Timeout = 2
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
    bool SuppressAutoBuildTests,
    ControlPlaneIdleCause IdleCause = ControlPlaneIdleCause.None,
    DateTimeOffset? LastActivityUtc = null);

public sealed record ControlPlaneWatchStatus(
    ControlPlaneWatchState Watch,
    int? Pid);

public sealed record ControlPlaneShipCheckRequest(
    string ProjectId,
    string? Configuration,
    string? Filter,
    bool? SuppressAutoBuildTests);

public sealed record ControlPlaneRebuildRequest(
    string ProjectId,
    string? Configuration);

public sealed record ControlPlaneRebuildResult(
    bool Ok,
    string Project,
    string Build,
    int ExitCode,
    IReadOnlyList<string> Failures,
    string? Log);

public sealed record ControlPlaneRunTestsRequest(
    string ProjectId,
    string? Configuration,
    string? Filter);

public sealed record ControlPlaneRunTestsResult(
    bool Ok,
    string Project,
    ControlPlaneTestCounts? Tests,
    IReadOnlyList<string> Failures,
    string? Log);

public sealed record ControlPlaneRunStopResult(
    bool Ok,
    bool WasRunning,
    int? ExitCode,
    ControlPlaneWatchStatus Watch);

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
    bool ShipCheckInProgress,
    bool AgentRebuildInProgress = false,
    ControlPlaneShipCheckPhase AgentRebuildPhase = ControlPlaneShipCheckPhase.None,
    ControlPlaneShipCheckOutcome LastAgentRebuildOutcome = ControlPlaneShipCheckOutcome.None,
    DateTimeOffset? LastAgentRebuildCompletedUtc = null,
    ControlPlaneIdleCause IdleCause = ControlPlaneIdleCause.None,
    bool AgentTestsInProgress = false,
    ControlPlaneShipCheckOutcome LastAgentTestsOutcome = ControlPlaneShipCheckOutcome.None,
    DateTimeOffset? LastAgentTestsCompletedUtc = null,
    ProjectBuildControlMode BuildControlMode = ProjectBuildControlMode.FileWatching,
    bool AutoBuildEnabled = true)
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
        ShipCheckInProgress: false,
        AgentRebuildInProgress: false,
        AgentRebuildPhase: ControlPlaneShipCheckPhase.None,
        LastAgentRebuildOutcome: ControlPlaneShipCheckOutcome.None,
        LastAgentRebuildCompletedUtc: null,
        IdleCause: ControlPlaneIdleCause.None,
        AgentTestsInProgress: false,
        LastAgentTestsOutcome: ControlPlaneShipCheckOutcome.None,
        LastAgentTestsCompletedUtc: null,
        BuildControlMode: ProjectBuildControlMode.FileWatching,
        AutoBuildEnabled: true);
}

public sealed record ControlPlaneModeStatus(
    string ProjectId,
    ProjectBuildControlMode Mode,
    string ModeWire,
    ProjectBuildControlMode? PreviousMode = null,
    string? PreviousModeWire = null);
