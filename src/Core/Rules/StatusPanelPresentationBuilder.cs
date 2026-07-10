using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Builds one cohesive presentation model for the hover status panel from health snapshots.
/// All regions (cards, side rail, header) derive from this single pass.
/// </summary>
public static class StatusPanelPresentationBuilder
{
    public static StatusPanelPresentation Build(
        IReadOnlyList<ProjectHealthSnapshot> snapshots,
        DateTimeOffset? panelDismissAtUtc,
        DateTimeOffset utcNow)
    {
        var active = snapshots.Where(s => s.IsActive).ToList();
        var cards = active.Select(s => BuildCard(s, utcNow)).ToList();
        var sideRail = BuildSideRail(active);
        var headerCountdown = StatusPanelHeaderCountdownFormatter.Format(snapshots, panelDismissAtUtc, utcNow);
        var (headerStillEditingProjectId, headerStillEditingToolTip) = ResolveHeaderStillEditing(
            active,
            cards,
            utcNow);

        return new StatusPanelPresentation(
            cards,
            sideRail,
            headerCountdown,
            headerStillEditingProjectId,
            headerStillEditingToolTip,
            active.Count);
    }

    private static (string? ProjectId, string? ToolTip) ResolveHeaderStillEditing(
        IReadOnlyList<ProjectHealthSnapshot> activeSnapshots,
        IReadOnlyList<StatusPanelCardPresentation> cards,
        DateTimeOffset utcNow)
    {
        foreach (var snapshot in activeSnapshots.OrderByDescending(s => s.LastChangedUtc))
        {
            if (!StatusPanelBuildVisibilityEvaluator.ShouldShowStillEditingButton(snapshot, utcNow))
            {
                continue;
            }

            var card = cards.FirstOrDefault(c =>
                string.Equals(c.ProjectId, snapshot.ProjectId, StringComparison.OrdinalIgnoreCase));
            return (snapshot.ProjectId, card?.StillEditingToolTip);
        }

        return (null, null);
    }

    private static StatusPanelCardPresentation BuildCard(ProjectHealthSnapshot snapshot, DateTimeOffset utcNow)
    {
        var statusLine = FormatStatusLine(snapshot);
        var showProgressChart = snapshot.ProgressSteps.Count > 0
            && snapshot.State is ProjectLifecycleState.Building
                or ProjectLifecycleState.Testing
                or ProjectLifecycleState.BuildFailed;
        var hasIssues = snapshot.ErrorCount > 0 || snapshot.WarningCount > 0;
        var showErrorPreview = !showProgressChart && !string.IsNullOrWhiteSpace(snapshot.LastErrorPreview);
        var showActivityIndicator = !showProgressChart
            && !showErrorPreview
            && snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing;

        return new StatusPanelCardPresentation(
            ProjectId: snapshot.ProjectId,
            Health: snapshot.Health,
            DisplayName: snapshot.DisplayName,
            StatusLine: statusLine,
            LastBuildLine: FormatLastBuildLine(snapshot, utcNow),
            EditGatingDetailText: string.IsNullOrWhiteSpace(snapshot.EditGatingDetailText)
                ? null
                : snapshot.EditGatingDetailText,
            ShowSiteReady: StatusPanelBuildVisibilityEvaluator.ShouldShowSiteReady(snapshot),
            ShowSiteAwaiting: StatusPanelBuildVisibilityEvaluator.ShouldShowSiteAwaiting(snapshot),
            ListenUrl: snapshot.ListenUrl,
            ShowProgressChart: showProgressChart,
            ProgressSteps: snapshot.ProgressSteps,
            ShowErrorPreview: showErrorPreview,
            ErrorPreview: snapshot.LastErrorPreview,
            ShowActivityIndicator: showActivityIndicator,
            ActivityState: snapshot.State,
            ShowIssueSummary: !showProgressChart && !showErrorPreview && !showActivityIndicator && hasIssues,
            ShowIssueSummaryBelowProgress: showProgressChart && hasIssues,
            ErrorCount: snapshot.ErrorCount,
            WarningCount: snapshot.WarningCount,
            ShowCopyErrorsButton: snapshot.ErrorCount > 0,
            ShowRestartButtons: snapshot.SupportsAppRestart,
            ShowRunTestsButton: true,
            ShowStillEditingButton: StatusPanelBuildVisibilityEvaluator.ShouldShowStillEditingButton(
                snapshot,
                utcNow),
            StillEditingToolTip: StatusPanelBuildVisibilityEvaluator.ShouldShowStillEditingButton(snapshot, utcNow)
                ? StatusPanelBuildVisibilityEvaluator.StillEditingToolTip(snapshot, utcNow)
                : null);
    }

    private static StatusPanelSideRailPresentation BuildSideRail(IReadOnlyList<ProjectHealthSnapshot> active)
    {
        var accentSnapshot = active.FirstOrDefault(StatusPanelAccentFormatter.ShouldShowAccentRail);
        if (accentSnapshot is not null)
        {
            return new StatusPanelSideRailPresentation(
                Mode: StatusPanelSideRailMode.Accent,
                AccentHealth: StatusPanelAccentFormatter.ResolveAccentHealth(accentSnapshot),
                ActivityLabel: StatusPanelAccentFormatter.FormatActivityLabel(accentSnapshot),
                IdleHealth: MonitorHealth.Unknown,
                IdleLabel: string.Empty,
                ShowWebReadyBadge: false);
        }

        var idleHealth = StatusPanelIdleRailFormatter.ResolveHealth(active);
        var webReady = StatusPanelIdleRailFormatter.ResolveWebReady(active);

        return new StatusPanelSideRailPresentation(
            Mode: StatusPanelSideRailMode.Idle,
            AccentHealth: MonitorHealth.Unknown,
            ActivityLabel: string.Empty,
            IdleHealth: idleHealth,
            IdleLabel: StatusPanelIdleRailFormatter.FormatIdleLabel(idleHealth, webReady),
            ShowWebReadyBadge: webReady && idleHealth == MonitorHealth.Green);
    }

    private static string FormatStatusLine(ProjectHealthSnapshot snapshot)
    {
        var statusLine = snapshot.IsRestarting
            ? "Restarting app…"
            : snapshot.State == ProjectLifecycleState.WaitingForEdits
                ? $"Waiting — {snapshot.HealthLabel}"
                : $"{snapshot.HealthLabel} — {snapshot.State}";

        var issueSuffix = !string.IsNullOrWhiteSpace(snapshot.IssueCountsText)
            ? $" · {snapshot.IssueCountsText}"
            : snapshot.ErrorCount > 0 || snapshot.WarningCount > 0
                ? $" · {snapshot.ErrorCount}e / {snapshot.WarningCount}w"
                : string.Empty;

        return statusLine + issueSuffix;
    }

    private static string FormatLastBuildLine(ProjectHealthSnapshot snapshot, DateTimeOffset utcNow)
    {
        var isBuilding = snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing;
        if (snapshot.LastBuildFinishedAtUtc is { } finished)
        {
            var time = finished.ToLocalTime().ToString("g");
            return isBuilding ? $"Last build: {time} (in progress…)" : $"Last build: {time}";
        }

        return isBuilding ? "Build in progress…" : "Last build: —";
    }
}
