namespace BuildMonitor.Core.Models;

public enum StatusPanelSideRailMode
{
    Idle = 0,
    Accent = 1
}

/// <summary>One labelled row in the compact status grid (BUILD / AGENT / CHANGES / LAST BUILD).</summary>
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
    Active = 4
}

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
    bool ShowControlPlaneSection = false);

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
