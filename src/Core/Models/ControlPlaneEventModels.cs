namespace BuildMonitor.Core.Models;

public enum ControlPlaneEventKind
{
    Busy = 0,
    IdleAgent = 1,
    IdleTimeout = 2,
    BuildBlocked = 3,
    Rebuild = 4,
    Tests = 5,
    ShipCheck = 6,
    RunStop = 7,
    WatchPause = 8,
    WatchResume = 9,
    ModeChanged = 10
}

public sealed record ControlPlaneEventRecord(
    string Id,
    string ProjectId,
    DateTimeOffset OccurredAtUtc,
    ControlPlaneEventKind Kind,
    string Summary,
    string? Detail = null);

public enum ControlPlaneWorkflowHealth
{
    Unknown = 0,
    Healthy = 1,
    Debouncing = 2,
    Busy = 3,
    ExtraBuilds = 4,
    BuildDuringBusy = 5,
    NoSessionApi = 6
}

/// <summary>Agent busy/idle workflow analysis for Build diagnostics.</summary>
public sealed record ControlPlaneWorkflowSnapshot(
    string ProjectId,
    ControlPlaneWorkflowHealth Health,
    string StatusText,
    string StatusDetail,
    string LastCycleSummary,
    int BuildsAfterLastIdle,
    int BuildsBlockedToday,
    int BuildsDuringLastBusy,
    IReadOnlyList<ControlPlaneEventRecord> RecentEvents)
{
    public static ControlPlaneWorkflowSnapshot Empty(string projectId) =>
        new(
            projectId,
            ControlPlaneWorkflowHealth.NoSessionApi,
            "No agent session yet",
            "No /session/busy calls recorded today.",
            "—",
            0,
            0,
            0,
            []);
}
