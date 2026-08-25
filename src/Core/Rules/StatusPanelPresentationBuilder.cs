using System.Globalization;
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
        DateTimeOffset utcNow)
    {
        foreach (var snapshot in activeSnapshots.OrderByDescending(s => s.LastChangedUtc))
        {
            if (!StatusPanelBuildVisibilityEvaluator.ShouldShowStillEditingButton(snapshot, utcNow))
            {
                continue;
            }

            return (
                snapshot.ProjectId,
                StatusPanelBuildVisibilityEvaluator.StillEditingToolTip(snapshot, utcNow));
        }

        return (null, null);
    }

    private static StatusPanelCardPresentation BuildCard(ProjectHealthSnapshot snapshot, DateTimeOffset utcNow)
    {
        var controlPlane = ControlPlaneStatusFormatter.Format(snapshot, utcNow);
        var showProgressChart = snapshot.ProgressSteps.Count > 0
            && snapshot.State is ProjectLifecycleState.Building
                or ProjectLifecycleState.Testing
                or ProjectLifecycleState.BuildFailed;
        var showErrorPreview = !showProgressChart && !string.IsNullOrWhiteSpace(snapshot.LastErrorPreview);
        var buildOverrideActive = !string.IsNullOrWhiteSpace(controlPlane.BuildActivityOverride);
        var showActivityIndicator = !showProgressChart
            && !showErrorPreview
            && !buildOverrideActive
            && snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing;

        var statusRows = BuildStatusRows(snapshot, controlPlane, utcNow);
        var currentAction = ResolveCurrentAction(snapshot, controlPlane, statusRows);
        var azurePresentation = snapshot.Azure is null
            ? null
            : AzureStatusPresentationBuilder.Build(
                snapshot.Azure,
                azureAttached: true,
                hasSelectedPipelines: snapshot.Azure.HasSelectedPipelines,
                utcNow);

        return new StatusPanelCardPresentation(
            ProjectId: snapshot.ProjectId,
            Health: snapshot.Health,
            DisplayName: snapshot.DisplayName,
            StatusRows: statusRows,
            CurrentActionText: currentAction,
            ShowSiteReady: StatusPanelBuildVisibilityEvaluator.ShouldShowSiteReady(snapshot),
            ShowSiteAwaiting: StatusPanelBuildVisibilityEvaluator.ShouldShowSiteAwaiting(snapshot),
            ListenUrl: snapshot.ListenUrl,
            ShowProgressChart: showProgressChart,
            ProgressSteps: snapshot.ProgressSteps,
            ShowErrorPreview: showErrorPreview,
            ErrorPreview: snapshot.LastErrorPreview,
            ShowActivityIndicator: showActivityIndicator,
            ActivityState: snapshot.State,
            ErrorCount: snapshot.ErrorCount,
            WarningCount: snapshot.WarningCount,
            ShowCopyErrorsButton: snapshot.ErrorCount > 0,
            ShowRestartButtons: snapshot.SupportsAppRestart,
            ShowRunTestsButton: true,
            ShowStillEditingButton: false,
            StillEditingToolTip: null,
            ShowControlPlaneSection: controlPlane.ShowControlPlaneSection,
            Azure: azurePresentation is { ShowSection: true } ? azurePresentation : null);
    }

    private static IReadOnlyList<StatusPanelStatusRow> BuildStatusRows(
        ProjectHealthSnapshot snapshot,
        ControlPlaneStatusFormatter.Presentation controlPlane,
        DateTimeOffset utcNow)
    {
        var rows = new List<StatusPanelStatusRow>(5);

        var modePrimary = controlPlane.ModePrimary
            ?? ProjectBuildControlModeWire.ToDisplayLabel(
                (snapshot.ControlPlane ?? ProjectControlPlaneSnapshot.Unused).BuildControlMode);
        rows.Add(new StatusPanelStatusRow("MODE", modePrimary));

        rows.Add(BuildBuildRow(snapshot, controlPlane));

        if (controlPlane.ShowControlPlaneSection
            && !string.IsNullOrWhiteSpace(controlPlane.AgentPrimary))
        {
            rows.Add(new StatusPanelStatusRow(
                "AGENT",
                controlPlane.AgentPrimary!,
                controlPlane.AgentSecondary,
                Emphasis: controlPlane.AgentEmphasis));
        }

        var changes = BuildChangesRow(snapshot, controlPlane, utcNow);
        if (changes is not null)
        {
            rows.Add(changes);
        }

        var lastBuild = BuildLastBuildRow(snapshot, utcNow);
        if (lastBuild is not null)
        {
            rows.Add(lastBuild);
        }

        return rows;
    }

    private static StatusPanelStatusRow BuildBuildRow(
        ProjectHealthSnapshot snapshot,
        ControlPlaneStatusFormatter.Presentation controlPlane)
    {
        var primary = ResolveBuildPrimary(snapshot, controlPlane);
        var secondary = FormatIssueCounts(snapshot.ErrorCount, snapshot.WarningCount);
        var emphasis = snapshot.Health switch
        {
            MonitorHealth.Red => StatusPanelRowEmphasis.Error,
            MonitorHealth.Amber => StatusPanelRowEmphasis.Warning,
            _ when snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing
                => StatusPanelRowEmphasis.Active,
            _ => StatusPanelRowEmphasis.Normal
        };

        return new StatusPanelStatusRow("BUILD", primary, secondary, Emphasis: emphasis);
    }

    private static string ResolveBuildPrimary(
        ProjectHealthSnapshot snapshot,
        ControlPlaneStatusFormatter.Presentation controlPlane)
    {
        if (!string.IsNullOrWhiteSpace(controlPlane.BuildActivityOverride))
        {
            return controlPlane.BuildActivityOverride!;
        }

        if (snapshot.IsRestarting)
        {
            return "Restarting";
        }

        if (snapshot.State == ProjectLifecycleState.Building)
        {
            var percent = TryResolveBuildPercent(snapshot.ProgressSteps);
            return percent is null ? "Building" : $"Building · {percent}%";
        }

        return snapshot.State switch
        {
            ProjectLifecycleState.Testing => "Testing",
            ProjectLifecycleState.WaitingForEdits => "Waiting",
            ProjectLifecycleState.BuildFailed => "Build failed",
            ProjectLifecycleState.TestFailed => "Tests failed",
            ProjectLifecycleState.Crashed => "Crashed",
            ProjectLifecycleState.Watching => snapshot.HealthLabel,
            ProjectLifecycleState.Running => snapshot.HealthLabel,
            ProjectLifecycleState.Idle => snapshot.HealthLabel,
            ProjectLifecycleState.TestOk => snapshot.HealthLabel,
            ProjectLifecycleState.BuildOk => snapshot.HealthLabel,
            _ => snapshot.HealthLabel
        };
    }

    private static int? TryResolveBuildPercent(IReadOnlyList<BuildProgressStep> steps)
    {
        if (steps.Count == 0)
        {
            return null;
        }

        var complete = steps.Count(s => s.Status == BuildStepStatus.Complete);
        var failed = steps.Any(s => s.Status == BuildStepStatus.Failed);
        if (failed)
        {
            return null;
        }

        return (int)Math.Round(100.0 * complete / steps.Count);
    }

    private static StatusPanelStatusRow? BuildChangesRow(
        ProjectHealthSnapshot snapshot,
        ControlPlaneStatusFormatter.Presentation controlPlane,
        DateTimeOffset utcNow)
    {
        var primary = controlPlane.ChangesPrimary;
        if (string.IsNullOrWhiteSpace(primary) && snapshot.IsEditGatingActive)
        {
            primary = "Settling";
        }

        if (string.IsNullOrWhiteSpace(primary)
            && !string.IsNullOrWhiteSpace(snapshot.EditGatingDetailText))
        {
            primary = "Queued";
        }

        if (string.IsNullOrWhiteSpace(primary))
        {
            return null;
        }

        var secondary = controlPlane.ChangesSecondary;
        if (string.IsNullOrWhiteSpace(secondary))
        {
            secondary = CompactEditGatingSecondary(snapshot, utcNow);
        }

        return new StatusPanelStatusRow(
            "CHANGES",
            primary!,
            string.IsNullOrWhiteSpace(secondary) ? null : secondary,
            ToolTip: snapshot.EditGatingDetailText);
    }

    private static string? CompactEditGatingSecondary(ProjectHealthSnapshot snapshot, DateTimeOffset utcNow)
    {
        var controlPlane = snapshot.ControlPlane;
        if (controlPlane is not null && !controlPlane.AutoBuildEnabled)
        {
            // AI Controlled: never show quiet/debounce countdown as an impending build.
            return null;
        }

        if (snapshot.RebuildQuietUntilUtc is { } until && until > utcNow)
        {
            var remainingMs = (int)(until - utcNow).TotalMilliseconds;
            if (remainingMs > 0)
            {
                return remainingMs < 1000
                    ? $"{remainingMs} ms remaining"
                    : $"{remainingMs / 1000.0:0.#}s remaining";
            }
        }

        var detail = snapshot.EditGatingDetailText;
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        if (detail.Contains("Quiet period restarted", StringComparison.OrdinalIgnoreCase))
        {
            return "Quiet period restarted";
        }

        if (detail.Contains("post-build cooldown", StringComparison.OrdinalIgnoreCase))
        {
            return "Post-build cooldown";
        }

        if (detail.Contains("waiting for the current build", StringComparison.OrdinalIgnoreCase))
        {
            return "Waiting for current build";
        }

        if (detail.Contains("waiting for tests", StringComparison.OrdinalIgnoreCase))
        {
            return "Waiting for tests";
        }

        if (detail.Contains("newer changes", StringComparison.OrdinalIgnoreCase))
        {
            return "Newer changes — will rebuild";
        }

        if (detail.Contains("Waiting", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("waiting", StringComparison.Ordinal))
        {
            return "Waiting for edits";
        }

        return null;
    }

    private static StatusPanelStatusRow? BuildLastBuildRow(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow)
    {
        if (snapshot.LastBuildFinishedAtUtc is not { } finished)
        {
            if (snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing)
            {
                return new StatusPanelStatusRow("LAST BUILD", "In progress…");
            }

            return null;
        }

        var absolute = finished.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var relative = FormatRelativeTime(utcNow - finished);
        return new StatusPanelStatusRow(
            "LAST BUILD",
            absolute,
            relative,
            ToolTip: finished.ToLocalTime().ToString("F", CultureInfo.CurrentCulture));
    }

    private static string FormatRelativeTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(45))
        {
            return "Just now";
        }

        if (elapsed < TimeSpan.FromMinutes(90))
        {
            var minutes = Math.Max(1, (int)elapsed.TotalMinutes);
            return $"{minutes}m ago";
        }

        if (elapsed < TimeSpan.FromHours(36))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        return $"{(int)elapsed.TotalDays}d ago";
    }

    private static string FormatIssueCounts(int errors, int warnings) =>
        $"{errors:N0} errors · {warnings:N0} warnings";

    private static string? ResolveCurrentAction(
        ProjectHealthSnapshot snapshot,
        ControlPlaneStatusFormatter.Presentation controlPlane,
        IReadOnlyList<StatusPanelStatusRow> rows)
    {
        if (!string.IsNullOrWhiteSpace(controlPlane.TransientAction))
        {
            return controlPlane.TransientAction;
        }

        if (snapshot.IsRestarting)
        {
            return "Restarting watch host…";
        }

        if (snapshot.State == ProjectLifecycleState.WaitingForEdits)
        {
            var alreadyCovered = rows.Any(r =>
                r.Label == "CHANGES"
                && (r.Secondary?.Contains("remaining", StringComparison.OrdinalIgnoreCase) == true
                    || r.Secondary?.Contains("Quiet period", StringComparison.OrdinalIgnoreCase) == true
                    || r.Secondary?.Contains("Waiting", StringComparison.OrdinalIgnoreCase) == true));
            return alreadyCovered ? null : "Waiting for edits to settle…";
        }

        return null;
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
}
