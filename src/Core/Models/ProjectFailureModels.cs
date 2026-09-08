namespace BuildMonitor.Core.Models;

/// <summary>Subsystem that owns a current failure reason (#111).</summary>
public enum FailureSourceKind
{
    LocalBuild = 0,
    LocalTests = 1,
    /// <summary>Reserved for #111b — not built in #111a.</summary>
    AzureCi = 2,
    /// <summary>Reserved for later Azure availability reasons.</summary>
    AzureAvailability = 3,
    /// <summary>Reserved for #111c — not built in #111a.</summary>
    RunHost = 4
}

public enum FailureSeverity
{
    Error = 0,
    Warning = 1
}

public enum FailureActionKind
{
    OpenBuildLog = 0,
    OpenTestLog = 1,
    CopyErrors = 2,
    Rebuild = 3,
    RebuildAndRestart = 4,
    RunTests = 5
}

/// <summary>One context-specific action on a failure reason.</summary>
public sealed record FailureAction(FailureActionKind Kind, string Label);

/// <summary>
/// One current authoritative failure reason. Distinct from activity (#112) and history (#110).
/// </summary>
public sealed record FailureReason(
    FailureSourceKind Source,
    string Title,
    string ShortReason,
    FailureSeverity Severity,
    IReadOnlyList<FailureAction> Actions,
    string? Detail = null,
    DateTimeOffset? ObservedAtUtc = null,
    int? ExitCode = null,
    int? LocalBuildNumber = null,
    string? BuildTriggerId = null,
    string? OperationId = null,
    BuildLogKind? LogKind = null);

/// <summary>
/// Current failure details for a project. Empty list is not represented — use null on presentation.
/// </summary>
public sealed record ProjectFailureDetails(IReadOnlyList<FailureReason> Reasons)
{
    public FailureReason Primary => Reasons[0];

    public const int MaxFailingTestNamesOnCard = 3;
}

/// <summary>
/// Structured result of the last completed failed Local test run (current-state, not history).
/// </summary>
public sealed record LocalTestFailureSnapshot(
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<string> FailingTestNames,
    string? FirstFailureMessage = null,
    string? OperationId = null);
