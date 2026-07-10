namespace BuildMonitor.Core.Models;

public enum StatusPanelSideRailMode
{
    Idle = 0,
    Accent = 1
}

/// <summary>Derived view of one project card in the hover status panel.</summary>
public sealed record StatusPanelCardPresentation(
    string ProjectId,
    MonitorHealth Health,
    string DisplayName,
    string StatusLine,
    string LastBuildLine,
    string? EditGatingDetailText,
    bool ShowSiteReady,
    bool ShowSiteAwaiting,
    string? ListenUrl,
    bool ShowProgressChart,
    IReadOnlyList<BuildProgressStep> ProgressSteps,
    bool ShowErrorPreview,
    string? ErrorPreview,
    bool ShowActivityIndicator,
    ProjectLifecycleState ActivityState,
    bool ShowIssueSummary,
    bool ShowIssueSummaryBelowProgress,
    int ErrorCount,
    int WarningCount,
    bool ShowCopyErrorsButton,
    bool ShowRestartButtons,
    bool ShowRunTestsButton,
    bool ShowStillEditingButton,
    string? StillEditingToolTip);

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
