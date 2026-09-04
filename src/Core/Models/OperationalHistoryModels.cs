namespace BuildMonitor.Core.Models;

/// <summary>Who originated an operational history event (not display text).</summary>
public enum OperationalEventSource
{
    Local = 0,
    Azure = 1,
    Agent = 2,
    User = 3,
    System = 4
}

/// <summary>Stable V1 event categories for operational history (#110 / #113).</summary>
public enum OperationalEventKind
{
    Build = 0,
    Tests = 1,
    RunHost = 2,
    WaitingForEdits = 3,
    ExplicitAction = 4,
    AzureRun = 5,
    HealthTransition = 6,
    WorkflowMode = 7
}

/// <summary>Result or phase marker for an operational event.</summary>
public enum OperationalEventOutcome
{
    None = 0,
    Started = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Changed = 5
}

/// <summary>
/// Sparse structured context for V1 failure/activity enrichment.
/// Prefer typed fields over free-form bags.
/// Emitters should keep <see cref="FailingTestNames"/> short; the store clamps to
/// <see cref="MaxFailingTestNames"/> on record.
/// </summary>
public sealed record OperationalEventDetail(
    int? ExitCode = null,
    string? ErrorPreview = null,
    BuildLogKind? LogKind = null,
    int? TestFailedCount = null,
    IReadOnlyList<string>? FailingTestNames = null,
    string? AzureStage = null,
    string? HoldReason = null,
    string? ActionName = null)
{
    /// <summary>Practical cap for names stored on an event (emitters should not exceed this).</summary>
    public const int MaxFailingTestNames = 5;
}

/// <summary>
/// Immutable operational history observation. Append-only; not authoritative runtime state.
/// SchemaVersion documents the JSONL contract (current = <see cref="OperationalHistorySchema.CurrentVersion"/>).
/// </summary>
public sealed record OperationalEvent(
    int SchemaVersion,
    string Id,
    string ProjectId,
    DateTimeOffset OccurredAtUtc,
    OperationalEventSource Source,
    OperationalEventKind Kind,
    OperationalEventOutcome Outcome,
    string Summary,
    OperationalEventDetail? Detail = null,
    string? OperationId = null,
    string? BuildTriggerId = null,
    int? LocalBuildNumber = null,
    long? AzureRunId = null,
    string? AzureBuildNumber = null,
    string? Branch = null,
    string? PreviousValue = null,
    string? NewValue = null);

/// <summary>JSONL / model schema version for operational history.</summary>
public static class OperationalHistorySchema
{
    public const int CurrentVersion = 1;
}
