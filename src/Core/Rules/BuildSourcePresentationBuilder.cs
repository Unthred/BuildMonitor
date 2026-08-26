using System.Globalization;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Maps Local and Azure runtime data into shared <see cref="BuildSourcePresentationRow"/> rows.</summary>
public static class BuildSourcePresentationBuilder
{
    public static bool HasLocalBuildSource(ProjectHealthSnapshot snapshot) =>
        snapshot.SupportsAppRestart
        || !string.IsNullOrWhiteSpace(snapshot.ListenUrl)
        || snapshot.LastBuildFinishedAtUtc is not null
        || snapshot.LastDuration is not null
        || snapshot.State is ProjectLifecycleState.Building
            or ProjectLifecycleState.Testing
            or ProjectLifecycleState.WaitingForEdits
            or ProjectLifecycleState.Watching
            or ProjectLifecycleState.Running
            or ProjectLifecycleState.BuildFailed
            or ProjectLifecycleState.TestFailed
            or ProjectLifecycleState.Crashed
            or ProjectLifecycleState.BuildOk
            or ProjectLifecycleState.TestOk;

    public static BuildSourcePresentationRow? TryBuildLocal(
        ProjectHealthSnapshot snapshot,
        ControlPlaneStatusFormatter.Presentation controlPlane,
        DateTimeOffset utcNow)
    {
        if (!HasLocalBuildSource(snapshot))
        {
            return null;
        }

        var localHealth = StatusPanelPresentationBuilder.ResolveLocalBuildHealth(snapshot);
        var statusText = ResolveLocalStatusText(snapshot, controlPlane, localHealth);
        var (glyph, emphasis) = ResolveLocalGlyph(snapshot, localHealth, statusText);

        return new BuildSourcePresentationRow(
            Source: "Local",
            StatusGlyph: glyph,
            StatusText: statusText,
            BranchDisplay: FormatLocalBranchDisplay(snapshot.LocalGit),
            RunDisplay: "—",
            BuildNumberDisplay: "—",
            PullRequestDisplay: "—",
            AgeDisplay: FormatLocalAge(snapshot, utcNow),
            IssuesDisplay: FormatIssues(snapshot.ErrorCount, snapshot.WarningCount),
            DeepLinkUrl: null,
            Emphasis: emphasis);
    }

    /// <summary>Local row Branch from local Git context only — never Azure FocusBranch.</summary>
    public static string FormatLocalBranchDisplay(LocalGitContext? localGit) =>
        localGit switch
        {
            { HeadStatus: LocalGitHeadStatus.Branch, CurrentBranch: { Length: > 0 } branch } => branch,
            { HeadStatus: LocalGitHeadStatus.Detached } => "detached",
            _ => "—"
        };

    public static IReadOnlyList<BuildSourcePresentationRow> BuildAzureRows(
        ProjectAzureHealthFacet? facet,
        bool azureAttached,
        bool hasSelectedPipelines,
        DateTimeOffset utcNow)
    {
        if (!azureAttached)
        {
            return [];
        }

        var azureUi = AzureStatusPresentationBuilder.Build(
            facet,
            azureAttached: true,
            hasSelectedPipelines,
            utcNow);

        if (!azureUi.ShowSection)
        {
            return [];
        }

        if (!azureUi.ShowTable)
        {
            var status = string.IsNullOrWhiteSpace(azureUi.MessagePrimary)
                ? "Unknown"
                : azureUi.MessagePrimary!;
            var glyph = string.IsNullOrWhiteSpace(azureUi.MessageGlyph) ? "○" : azureUi.MessageGlyph!;
            return
            [
                new BuildSourcePresentationRow(
                    Source: "Azure",
                    StatusGlyph: glyph,
                    StatusText: status,
                    BranchDisplay: "—",
                    RunDisplay: "—",
                    BuildNumberDisplay: "—",
                    PullRequestDisplay: "—",
                    AgeDisplay: string.IsNullOrWhiteSpace(azureUi.MessageSecondary)
                        ? "—"
                        : azureUi.MessageSecondary!,
                    IssuesDisplay: "—",
                    DeepLinkUrl: null,
                    Emphasis: azureUi.Emphasis)
            ];
        }

        var primary = azureUi.Rows[0];
        // Previous-failure attention stays on the facet for future notifications; do not
        // surface it under the current BUILDS row (at-a-glance current state only).

        var age = string.IsNullOrWhiteSpace(primary.TimingText)
            ? FormatAzureCompletedAge(facet?.PrimaryRun, utcNow)
            : CompactTiming(primary.TimingText!);

        return
        [
            new BuildSourcePresentationRow(
                Source: "Azure",
                StatusGlyph: primary.StatusGlyph,
                StatusText: primary.StatusText,
                BranchDisplay: string.IsNullOrWhiteSpace(primary.Branch) ? "—" : primary.Branch,
                RunDisplay: primary.RunDisplay,
                BuildNumberDisplay: primary.BuildNumberDisplay,
                PullRequestDisplay: primary.PullRequestDisplay,
                AgeDisplay: age,
                IssuesDisplay: "—",
                DeepLinkUrl: primary.RunUrl,
                Emphasis: primary.Emphasis,
                AttentionNote: null)
        ];
    }

    public static IReadOnlyList<BuildSourcePresentationRow> BuildAll(
        ProjectHealthSnapshot snapshot,
        ControlPlaneStatusFormatter.Presentation controlPlane,
        DateTimeOffset utcNow)
    {
        var list = new List<BuildSourcePresentationRow>(2);
        var local = TryBuildLocal(snapshot, controlPlane, utcNow);
        if (local is not null)
        {
            list.Add(local);
        }

        if (snapshot.Azure is not null)
        {
            list.AddRange(BuildAzureRows(
                snapshot.Azure,
                azureAttached: true,
                snapshot.Azure.HasSelectedPipelines,
                utcNow));
        }

        return list;
    }

    private static string ResolveLocalStatusText(
        ProjectHealthSnapshot snapshot,
        ControlPlaneStatusFormatter.Presentation controlPlane,
        MonitorHealth localHealth)
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
            var percent = TryBuildPercent(snapshot.ProgressSteps);
            return percent is null ? "Building" : $"Building · {percent}%";
        }

        return snapshot.State switch
        {
            ProjectLifecycleState.Testing => "Testing",
            ProjectLifecycleState.WaitingForEdits => "Waiting",
            ProjectLifecycleState.BuildFailed => "Build failed",
            ProjectLifecycleState.TestFailed => "Tests failed",
            ProjectLifecycleState.Crashed => "Crashed",
            _ => StripStatusGlyph(StatusPanelPresentationBuilder.FormatSettledLocalBuildPrimary(localHealth))
        };
    }

    private static string StripStatusGlyph(string text) =>
        text.StartsWith("✓ ", StringComparison.Ordinal) ? text[2..] : text;

    private static int? TryBuildPercent(IReadOnlyList<BuildProgressStep> steps)
    {
        if (steps.Count == 0)
        {
            return null;
        }

        var complete = steps.Count(s => s.Status == BuildStepStatus.Complete);
        if (steps.Any(s => s.Status == BuildStepStatus.Failed))
        {
            return null;
        }

        return (int)Math.Round(100.0 * complete / steps.Count);
    }

    private static (string Glyph, StatusPanelRowEmphasis Emphasis) ResolveLocalGlyph(
        ProjectHealthSnapshot snapshot,
        MonitorHealth localHealth,
        string statusText)
    {
        if (snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing
            || snapshot.IsRestarting
            || statusText.Contains("Building", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Testing", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Ship check", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Agent rebuild", StringComparison.OrdinalIgnoreCase))
        {
            return ("◉", StatusPanelRowEmphasis.Active);
        }

        return localHealth switch
        {
            MonitorHealth.Green => ("✓", StatusPanelRowEmphasis.Success),
            MonitorHealth.Amber => ("!", StatusPanelRowEmphasis.Warning),
            MonitorHealth.Red => ("✕", StatusPanelRowEmphasis.Error),
            _ => ("○", StatusPanelRowEmphasis.Normal)
        };
    }

    private static string FormatLocalAge(ProjectHealthSnapshot snapshot, DateTimeOffset utcNow)
    {
        if (snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing)
        {
            return "In progress";
        }

        if (snapshot.LastBuildFinishedAtUtc is not { } finished)
        {
            return "—";
        }

        var age = FormatRelativeShort(utcNow - finished);
        if (snapshot.LastDuration is { } duration && duration > TimeSpan.Zero)
        {
            return $"{age} · {FormatDurationShort(duration)}";
        }

        return age;
    }

    private static string FormatAzureCompletedAge(AzurePipelineRunInfo? run, DateTimeOffset utcNow)
    {
        if (run is null)
        {
            return "—";
        }

        if (AzureRunSelector.IsActive(run.State))
        {
            var start = run.StartedAtUtc ?? run.QueuedAtUtc;
            return CompactTiming("Running " + AzureStatusPresentationBuilder.FormatDuration(utcNow - start));
        }

        if (run.FinishedAtUtc is { } finished)
        {
            var age = FormatRelativeShort(utcNow - finished);
            if (run.StartedAtUtc is { } started && finished > started)
            {
                return $"{age} · {FormatDurationShort(finished - started)}";
            }

            return age;
        }

        return "—";
    }

    private static string CompactTiming(string timingText) =>
        timingText.StartsWith("Running ", StringComparison.Ordinal)
            ? timingText["Running ".Length..]
            : timingText;

    private static string FormatRelativeShort(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromSeconds(45))
        {
            return "now";
        }

        if (elapsed < TimeSpan.FromMinutes(90))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m";
        }

        if (elapsed < TimeSpan.FromHours(36))
        {
            return $"{(int)elapsed.TotalHours}h";
        }

        return $"{(int)elapsed.TotalDays}d";
    }

    private static string FormatDurationShort(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)duration.TotalHours}h{duration.Minutes}m");
        }

        if (duration.TotalMinutes >= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)duration.TotalMinutes}m{duration.Seconds}s");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Max(1, duration.TotalSeconds):0.#}s");
    }

    private static string FormatIssues(int errors, int warnings) =>
        string.Create(CultureInfo.InvariantCulture, $"{errors}E · {warnings}W");
}
