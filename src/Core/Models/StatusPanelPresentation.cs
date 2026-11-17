namespace BuildMonitor.Core.Models;

public enum StatusPanelSideRailMode
{
    Idle = 0,
    Accent = 1
}

/// <summary>One labelled row in DETAIL (MODE / AGENT / CHANGES).</summary>
public sealed record StatusPanelStatusRow(
    string Label,
    string Primary,
    string? Secondary = null,
    string? ToolTip = null,
    StatusPanelRowEmphasis Emphasis = StatusPanelRowEmphasis.Normal);

public enum StatusPanelRowEmphasis
{
    Normal = 0,
    Busy = 1,
    Warning = 2,
    Error = 3,
    Active = 4,
    /// <summary>Settled success (Succeeded / Healthy) — green semantic colour.</summary>
    Success = 5
}

/// <summary>
/// Shared presentation row for Local and Azure build sources (visual parity).
/// Azure-only fields stay blank for Local rather than fabricated.
/// </summary>
public sealed record BuildSourcePresentationRow(
    string Source,
    string StatusGlyph,
    string StatusText,
    string BranchDisplay,
    string RunDisplay,
    string BuildNumberDisplay,
    string PullRequestDisplay,
    string AgeDisplay,
    string IssuesDisplay,
    AzureBuildSourceNavigation? AzureNavigation,
    StatusPanelRowEmphasis Emphasis,
    string? AttentionNote = null);

/// <summary>Derived view of one project card in the hover status panel.</summary>
public sealed record StatusPanelCardPresentation(
    string ProjectId,
    MonitorHealth Health,
    string DisplayName,
    IReadOnlyList<StatusPanelStatusRow> StatusRows,
    string? CurrentActionText,
    bool ShowSiteReady,
    bool ShowSiteAwaiting,
    string? ListenUrl,
    bool ShowProgressChart,
    IReadOnlyList<BuildProgressStep> ProgressSteps,
    bool ShowErrorPreview,
    string? ErrorPreview,
    bool ShowActivityIndicator,
    ProjectLifecycleState ActivityState,
    int ErrorCount,
    int WarningCount,
    bool ShowCopyErrorsButton,
    bool ShowRebuildButton,
    bool ShowRestartButtons,
    bool ShowRunTestsButton,
    bool ShowStillEditingButton,
    string? StillEditingToolTip,
    MonitorHealth OverallHealth,
    string OverallLabel,
    bool ShowControlPlaneSection = false,
    AzureStatusPresentation? Azure = null,
    IReadOnlyList<BuildSourcePresentationRow>? BuildSourceRows = null,
    OperationalHistorySectionPresentation? RecentActivity = null,
    ProjectActivitySet? Activity = null,
    /// <summary>Current Local failure details (#111a); null when no current failure reasons.</summary>
    ProjectFailureDetails? FailureDetails = null,
    /// <summary>Why Overall needs attention, e.g. "14 warnings" (#167).</summary>
    string? OverallReason = null,
    string? OverallToolTip = null,
    /// <summary>Automatic start held this project back; offer an explicit Start (#167).</summary>
    bool ShowStartButton = false);

/// <summary>Derived view of the right-hand status rail.</summary>
public sealed record StatusPanelSideRailPresentation(
    StatusPanelSideRailMode Mode,
    MonitorHealth AccentHealth,
    string ActivityLabel,
    MonitorHealth IdleHealth,
    string IdleLabel,
    bool ShowWebReadyBadge,
    /// <summary>Why Overall needs attention, e.g. "28 warnings" (#167).</summary>
    string? IdleReason = null,
    string? IdleToolTip = null);

/// <summary>Single derived model for the entire hover status panel.</summary>
public sealed record StatusPanelPresentation(
    IReadOnlyList<StatusPanelCardPresentation> Cards,
    StatusPanelSideRailPresentation SideRail,
    string HeaderCountdownText,
    string? HeaderStillEditingProjectId,
    string? HeaderStillEditingToolTip,
    int ActiveProjectCount);

/// <summary>
/// Overall headline (Healthy / Attention / Needs fix / …) with the cause for Attention
/// (for example "14 warnings") and a per-project tooltip. <see cref="Reason"/> is short;
/// <see cref="ToolTip"/> holds the detail.
/// </summary>
public sealed record StatusPanelOverallSummary(string Label, string? Reason = null, string? ToolTip = null)
{
    /// <summary>Single-line form, e.g. "Attention · 14 warnings".</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Reason) ? Label : $"{Label} · {Reason}";
}
