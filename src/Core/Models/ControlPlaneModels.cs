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
    bool IsActiveInSession,
    MonitorHealth? OverallHealth = null,
    string? OverallHealthLabel = null,
    ControlPlaneSessionState? SessionState = null,
    ControlPlaneLocalFacetInfo? Local = null,
    ControlPlaneAzureFacetInfo? Azure = null,
    /// <summary>
    /// Authoritative current activities from #112 (<c>ProjectActivityBuilder</c>).
    /// Empty means no current activity (not a synthetic idle record). Always present on the wire.
    /// </summary>
    IReadOnlyList<ControlPlaneActivityInfo> Activities = null!,
    /// <summary>Primary activity summary (<c>ProjectActivitySet.PrimaryStatusText</c>); omitted when null.</summary>
    string? ActivitySummary = null);

/// <summary>Wire DTO for one #112 activity on <c>GET /projects</c>.</summary>
public sealed record ControlPlaneActivityInfo(
    ActivitySourceKind Source,
    ActivityPhaseKind Phase,
    string Summary,
    string? Detail = null,
    DateTimeOffset? StartedAtUtc = null,
    ControlPlaneActivityProgressInfo? Progress = null,
    string? OperationId = null,
    long? AzureRunId = null,
    string? AzureBuildNumber = null,
    string? Branch = null);

/// <summary>
/// Trustworthy progress only. <see cref="Total"/> omitted when unknown (e.g. mid-run completed count).
/// Never invent percentages or ETAs from a missing total.
/// </summary>
public sealed record ControlPlaneActivityProgressInfo(int Current, int? Total = null);

/// <summary>Local build facet on <c>GET /projects</c> — from the tray snapshot, not a live rebuild.</summary>
public sealed record ControlPlaneLocalFacetInfo(
    MonitorHealth Status,
    string? Branch,
    DateTimeOffset? LastBuildAtUtc,
    int Errors,
    int Warnings,
    ProjectLifecycleState LifecycleState,
    int? LastBuildExitCode = null);

/// <summary>
/// Azure CI facet on <c>GET /projects</c>.
/// <see cref="RunId"/> is Azure Build.id; <see cref="BuildNumber"/> is Azure buildNumber — never conflate them.
/// Primary run matches the hover status panel (facet <c>PrimaryRun</c>), not independent newest-run selection.
/// </summary>
public sealed record ControlPlaneAzureFacetInfo(
    AzureMonitoringAvailability Availability,
    AzureCiMonitoringState CiState,
    string? Pipeline,
    string? Status,
    string? Branch,
    long? RunId,
    string? BuildNumber,
    int? PullRequestNumber,
    string? RunUrl,
    DateTimeOffset PolledAtUtc,
    int? AgeSeconds = null,
    string? StatusMessage = null,
    bool HasSelectedPipelines = true,
    string? AttentionSummary = null,
    string? FocusBranch = null);

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

/// <summary>
/// Terminal classification for a completed <c>/run/rebuild</c>, <c>/run/tests</c>, or <c>/run/ship-check</c>.
/// Present only on HTTP 200 result bodies — never on 400/404/409/500 disposition responses.
/// </summary>
public enum ControlPlaneOperationOutcome
{
    Succeeded = 0,
    BuildFailed = 1,
    TestsFailed = 2,
    NoTests = 3,
    ExecutionFailed = 4,
    Cancelled = 5
}

public sealed record ControlPlaneRebuildResult(
    bool Ok,
    string Project,
    string Build,
    int ExitCode,
    IReadOnlyList<string> Failures,
    string? Log,
    ControlPlaneOperationOutcome Outcome);

public sealed record ControlPlaneRunTestsRequest(
    string ProjectId,
    string? Configuration,
    string? Filter);

public sealed record ControlPlaneRunTestsResult(
    bool Ok,
    string Project,
    ControlPlaneTestCounts? Tests,
    IReadOnlyList<string> Failures,
    string? Log,
    ControlPlaneOperationOutcome Outcome);

public sealed record ControlPlaneRunStopResult(
    bool Ok,
    bool WasRunning,
    int? ExitCode,
    ControlPlaneWatchStatus Watch);

public sealed record ControlPlaneCancelRequest(
    string ProjectId,
    string? OperationId);

/// <summary>
/// Disposition for <c>POST /run/cancel</c>. <c>Ok</c> means the cancel signal was accepted —
/// not that the original <c>/run/*</c> operation succeeded.
/// </summary>
public sealed record ControlPlaneCancelResult(
    bool Ok,
    string Project,
    string OperationId,
    ControlPlaneOperationKind OperationKind,
    bool CancelRequested,
    bool AlreadyRequested);

public sealed record ControlPlaneTestCounts(int Failed, int Passed, int Skipped);

/// <summary>
/// Structured evidence for classifying a completed test phase — not derived from <c>failures[]</c> prose.
/// </summary>
public sealed record ControlPlaneTestPhaseEvidence(
    bool LifecycleTestOk,
    bool NoTargetsConfigured,
    ControlPlaneTestCounts? Counts);

public sealed record ControlPlaneShipCheckResult(
    bool Ok,
    string Project,
    string Build,
    ControlPlaneTestCounts? Tests,
    IReadOnlyList<string> Failures,
    string? Log,
    ControlPlaneOperationOutcome Outcome);

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
    bool AutoBuildEnabled = true,
    string? ActiveOperationId = null,
    ControlPlaneOperationKind? ActiveOperationKind = null,
    bool OperationCancelRequested = false)
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
        AutoBuildEnabled: true,
        ActiveOperationId: null,
        ActiveOperationKind: null,
        OperationCancelRequested: false);
}

public sealed record ControlPlaneModeStatus(
    string ProjectId,
    ProjectBuildControlMode Mode,
    string ModeWire,
    ProjectBuildControlMode? PreviousMode = null,
    string? PreviousModeWire = null);
