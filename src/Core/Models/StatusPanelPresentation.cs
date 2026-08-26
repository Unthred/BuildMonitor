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
    string? DeepLinkUrl,
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
    bool ShowRestartButtons,
    bool ShowRunTestsButton,
    bool ShowStillEditingButton,
    string? StillEditingToolTip,
    bool ShowControlPlaneSection = false,
    AzureStatusPresentation? Azure = null,
    IReadOnlyList<BuildSourcePresentationRow>? BuildSourceRows = null);

/// <summary>Derived view of the right-hand status rail.</summary>
public sealed record StatusPanelSideRailPresentation(
    StatusPanelSideRailMode Mode,
    MonitorHealth AccentHealth,
    string ActivityLabel,
    MonitorHealth IdleHealth,
    string IdleLabel,
    bool ShowWebReadyBadge);

/// <summary>Single derived model for the entire hover status panel.</summary>
public sealed record StatusPanelPresentation(
    IReadOnlyList<StatusPanelCardPresentation> Cards,
    StatusPanelSideRailPresentation SideRail,
    string HeaderCountdownText,
    string? HeaderStillEditingProjectId,
    string? HeaderStillEditingToolTip,
    int ActiveProjectCount);
