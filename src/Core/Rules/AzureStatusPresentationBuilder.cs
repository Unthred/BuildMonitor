using System.Globalization;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Pure presentation mapping for the status-panel Azure section.</summary>
public static class AzureStatusPresentationBuilder
{
    public static AzureStatusPresentation Build(
        ProjectAzureHealthFacet? facet,
        bool azureAttached,
        bool hasSelectedPipelines,
        DateTimeOffset utcNow)
    {
        if (!azureAttached)
        {
            return Hidden();
        }

        if (!hasSelectedPipelines)
        {
            return new AzureStatusPresentation(
                ShowSection: true,
                HeaderLabel: "AZURE",
                Glyph: "○",
                PrimaryLine: "Connected · Not monitored",
                SecondaryLine: null,
                AttentionLine: null,
                RunUrl: null,
                Emphasis: StatusPanelRowEmphasis.Normal);
        }

        if (facet is null)
        {
            return new AzureStatusPresentation(
                ShowSection: true,
                HeaderLabel: "AZURE",
                Glyph: "…",
                PrimaryLine: "Checking…",
                SecondaryLine: null,
                AttentionLine: null,
                RunUrl: null,
                Emphasis: StatusPanelRowEmphasis.Normal);
        }

        if (facet.Availability == AzureMonitoringAvailability.AuthRequired)
        {
            return new AzureStatusPresentation(
                ShowSection: true,
                HeaderLabel: "AZURE",
                Glyph: "!",
                PrimaryLine: "Authentication required",
                SecondaryLine: facet.StatusMessage,
                AttentionLine: null,
                RunUrl: null,
                Emphasis: StatusPanelRowEmphasis.Warning);
        }

        if (facet.Availability == AzureMonitoringAvailability.Unavailable)
        {
            var ago = FormatRelativeAgo(utcNow - facet.PolledAtUtc);
            return new AzureStatusPresentation(
                ShowSection: true,
                HeaderLabel: "AZURE",
                Glyph: "!",
                PrimaryLine: "Azure DevOps unavailable",
                SecondaryLine: string.IsNullOrWhiteSpace(facet.StatusMessage)
                    ? $"Last checked {ago}"
                    : Truncate(facet.StatusMessage, 120),
                AttentionLine: null,
                RunUrl: null,
                Emphasis: StatusPanelRowEmphasis.Warning);
        }

        if (facet.PrimaryRun is null)
        {
            if (facet.PolledAtUtc == DateTimeOffset.MinValue)
            {
                return new AzureStatusPresentation(
                    ShowSection: true,
                    HeaderLabel: "AZURE",
                    Glyph: "…",
                    PrimaryLine: "Checking…",
                    SecondaryLine: null,
                    AttentionLine: null,
                    RunUrl: null,
                    Emphasis: StatusPanelRowEmphasis.Normal);
            }

            return new AzureStatusPresentation(
                ShowSection: true,
                HeaderLabel: "AZURE",
                Glyph: "○",
                PrimaryLine: "No runs",
                SecondaryLine: facet.FocusBranch is null ? null : $"Focus · {facet.FocusBranch}",
                AttentionLine: FormatAttention(facet.AttentionRuns),
                RunUrl: null,
                Emphasis: StatusPanelRowEmphasis.Normal);
        }

        var run = facet.PrimaryRun;
        var (glyph, emphasis, stateLabel) = DescribeRun(run);
        var detail = FormatRunDetail(run, utcNow);
        var attention = FormatAttention(facet.AttentionRuns);

        return new AzureStatusPresentation(
            ShowSection: true,
            HeaderLabel: "AZURE",
            Glyph: glyph,
            PrimaryLine: $"{run.PipelineDisplayName}",
            SecondaryLine: $"{stateLabel} · {run.Branch} · {detail}",
            AttentionLine: attention,
            RunUrl: string.IsNullOrWhiteSpace(run.RunUrl) ? null : run.RunUrl,
            Emphasis: emphasis);
    }

    public static (string Glyph, StatusPanelRowEmphasis Emphasis, string StateLabel) DescribeRun(AzurePipelineRunInfo run)
    {
        if (AzureRunSelector.IsActive(run.State))
        {
            var label = run.State switch
            {
                PipelineRunState.NotStarted => "Queued",
                PipelineRunState.Canceling => "Cancelling",
                _ => "Building"
            };
            return ("◉", StatusPanelRowEmphasis.Busy, label);
        }

        if (run.State == PipelineRunState.Completed)
        {
            return run.Result switch
            {
                PipelineRunResult.Succeeded => ("✓", StatusPanelRowEmphasis.Normal, "Succeeded"),
                PipelineRunResult.PartiallySucceeded => ("!", StatusPanelRowEmphasis.Warning, "Partially succeeded"),
                PipelineRunResult.Failed => ("✕", StatusPanelRowEmphasis.Error, "Failed"),
                PipelineRunResult.Canceled => ("○", StatusPanelRowEmphasis.Normal, "Cancelled"),
                _ => ("○", StatusPanelRowEmphasis.Normal, "Completed")
            };
        }

        return ("○", StatusPanelRowEmphasis.Normal, "Unknown");
    }

    public static string FormatRunDetail(AzurePipelineRunInfo run, DateTimeOffset utcNow)
    {
        if (AzureRunSelector.IsActive(run.State))
        {
            var start = run.StartedAtUtc ?? run.QueuedAtUtc;
            return FormatDuration(utcNow - start);
        }

        if (!string.IsNullOrWhiteSpace(run.BuildNumber))
        {
            return $"Build {run.BuildNumber}";
        }

        return run.RunId > 0 ? $"Build {run.RunId}" : "—";
    }

    public static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalHours >= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m");
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)elapsed.TotalSeconds)}s");
    }

    private static string? FormatAttention(IReadOnlyList<AzurePipelineRunInfo> attention)
    {
        if (attention.Count == 0)
        {
            return null;
        }

        var failed = attention.Count(r =>
            r.State == PipelineRunState.Completed && r.Result == PipelineRunResult.Failed);
        if (failed > 0)
        {
            return failed == 1
                ? "✕ 1 other pipeline failed"
                : $"✕ {failed} other pipelines failed";
        }

        var warnings = attention.Count(r =>
            r.State == PipelineRunState.Completed && r.Result == PipelineRunResult.PartiallySucceeded);
        if (warnings > 0)
        {
            return warnings == 1
                ? "! 1 other pipeline warning"
                : $"! {warnings} other pipelines warning";
        }

        var active = attention.Count(r => AzureRunSelector.IsActive(r.State));
        if (active > 0)
        {
            return active == 1
                ? "◉ 1 other pipeline running"
                : $"◉ {active} other pipelines running";
        }

        return null;
    }

    private static string FormatRelativeAgo(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)}m ago";
        }

        return $"{Math.Max(1, (int)age.TotalHours)}h ago";
    }

    private static string Truncate(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }

    private static AzureStatusPresentation Hidden() =>
        new(false, "AZURE", string.Empty, string.Empty, null, null, null, StatusPanelRowEmphasis.Normal);
}
