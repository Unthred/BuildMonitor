namespace BuildMonitor.Core.Models;

/// <summary>Who is driving the current activity (presentation-neutral).</summary>
public enum ActivitySourceKind
{
    Local = 0,
    Azure = 1,
    Agent = 2,
    System = 3
}

/// <summary>Coarse phase for “what is it doing right now?” — not health.</summary>
public enum ActivityPhaseKind
{
    Idle = 0,
    WaitingForEdits = 1,
    Building = 2,
    Testing = 3,
    Publishing = 4,
    RestartingHost = 5,
    StartingSite = 6,
    ShipCheck = 7,
    AgentRebuild = 8,
    AgentTests = 9,
    AzureQueued = 10,
    AzureInProgress = 11,
    AzureCanceling = 12,
    Reconnecting = 13,
    Working = 14,
    /// <summary>Agent/local control-plane operation is cancelling (not Azure).</summary>
    Cancelling = 15
}

/// <summary>
/// Trustworthy numeric progress only. Both ends must be known from the underlying operation;
/// never invent totals or percentages from elapsed time.
/// </summary>
public sealed record ActivityProgress(int Current, int Total)
{
    public double? Fraction => Total > 0 ? Math.Clamp(Current / (double)Total, 0d, 1d) : null;

    /// <summary>
    /// Creates progress when <paramref name="total"/> is positive.
    /// Clamps <paramref name="current"/> into <c>[0, total]</c>; does not invent a total.
    /// </summary>
    public static ActivityProgress? TryCreate(int current, int total)
    {
        if (total <= 0)
        {
            return null;
        }

        return new ActivityProgress(Math.Clamp(current, 0, total), total);
    }
}

/// <summary>
/// Live Local/Agent test counters while <see cref="ProjectLifecycleState.Testing"/>.
/// <see cref="Total"/> is set only from an authoritative summary (usually end-of-assembly);
/// mid-run VSTest console output typically exposes completed counts only.
/// </summary>
public sealed record TestRunLiveProgress(
    int Completed,
    DateTimeOffset StartedAtUtc,
    int? Total = null);

/// <summary>
/// Authoritative ephemeral activity for one project source. Distinct from operational history
/// (append-only “what happened?”) and from <see cref="MonitorHealth"/> (precedence unchanged).
/// </summary>
public sealed record ProjectActivitySnapshot(
    string ProjectId,
    ActivitySourceKind Source,
    ActivityPhaseKind Phase,
    string StatusText,
    DateTimeOffset UpdatedAtUtc,
    bool IsActive,
    DateTimeOffset? StartedAtUtc = null,
    ActivityProgress? Progress = null,
    string? OperationId = null,
    string? Detail = null,
    long? AzureRunId = null,
    string? AzureBuildNumber = null,
    string? Branch = null);

/// <summary>
/// Per-project activity set. Local and Azure may both be active; health remains separate.
/// </summary>
public sealed record ProjectActivitySet(
    string ProjectId,
    IReadOnlyList<ProjectActivitySnapshot> Activities,
    ProjectActivitySnapshot? Primary,
    string? PrimaryStatusText,
    string? CoexistenceSummaryText);
