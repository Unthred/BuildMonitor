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
            return Message("○", "Connected · Not monitored", null, StatusPanelRowEmphasis.Normal);
        }

        if (facet is null)
        {
            return Message("…", "Checking…", null, StatusPanelRowEmphasis.Normal);
        }

        if (facet.Availability == AzureMonitoringAvailability.AuthRequired)
        {
            return Message("!", "Authentication required", facet.StatusMessage, StatusPanelRowEmphasis.Warning);
        }

        if (facet.Availability == AzureMonitoringAvailability.Unavailable)
        {
            var ago = FormatRelativeAgo(utcNow - facet.PolledAtUtc);
            return Message(
                "!",
                "Azure DevOps unavailable",
                string.IsNullOrWhiteSpace(facet.StatusMessage)
                    ? $"Last checked {ago}"
                    : Truncate(facet.StatusMessage, 120),
                StatusPanelRowEmphasis.Warning);
        }

        if (facet.PrimaryRun is null)
        {
            if (facet.PolledAtUtc == DateTimeOffset.MinValue)
            {
                return Message("…", "Checking…", null, StatusPanelRowEmphasis.Normal);
            }

            return Message(
                "○",
                "No runs",
                facet.FocusBranch is null ? null : $"Focus · {facet.FocusBranch}",
                StatusPanelRowEmphasis.Normal,
                attention: FormatAttention(facet.AttentionRuns));
        }

        var rows = new List<AzureStatusTableRow> { ToTableRow(facet.PrimaryRun, utcNow) };
        foreach (var attentionRun in facet.AttentionRuns)
        {
            if (!ShouldShowAttentionAsRow(attentionRun))
            {
                continue;
            }

            rows.Add(ToTableRow(attentionRun, utcNow));
            if (rows.Count >= 3)
            {
                break;
            }
        }

        var shownRunIds = rows
            .Select(r => r.RunDisplay)
            .ToHashSet(StringComparer.Ordinal);
        var hiddenAttention = facet.AttentionRuns
            .Where(r => !shownRunIds.Contains(FormatRunId(r.RunId)))
            .ToList();

        return new AzureStatusPresentation(
            ShowSection: true,
            HeaderLabel: "AZURE DEVOPS",
            ShowTable: true,
            MessageGlyph: null,
            MessagePrimary: null,
            MessageSecondary: null,
            Rows: rows,
            AttentionLine: FormatAttention(hiddenAttention),
            PrimaryRunUrl: string.IsNullOrWhiteSpace(facet.PrimaryRun.RunUrl) ? null : facet.PrimaryRun.RunUrl,
            Emphasis: rows[0].Emphasis);
    }

    public static AzureStatusTableRow ToTableRow(AzurePipelineRunInfo run, DateTimeOffset utcNow)
    {
        var (glyph, emphasis, stateLabel) = DescribeRun(run);
        return new AzureStatusTableRow(
            Pipeline: run.PipelineDisplayName,
            StatusGlyph: glyph,
            StatusText: stateLabel,
            Branch: run.Branch,
            RunDisplay: FormatRunId(run.RunId),
            BuildNumberDisplay: FormatBuildNumber(run.BuildNumber),
            PullRequestDisplay: FormatPullRequest(run.PullRequestNumber),
            TimingText: FormatTiming(run, utcNow),
            RunUrl: string.IsNullOrWhiteSpace(run.RunUrl) ? null : run.RunUrl,
            Emphasis: emphasis);
    }

    public static string FormatRunId(long runId) =>
        runId > 0 ? string.Create(CultureInfo.InvariantCulture, $"#{runId}") : "—";

    public static string FormatBuildNumber(string? buildNumber) =>
        string.IsNullOrWhiteSpace(buildNumber) ? "—" : buildNumber.Trim();

    public static string FormatPullRequest(int? pullRequestNumber) =>
        pullRequestNumber is > 0
            ? string.Create(CultureInfo.InvariantCulture, $"#{pullRequestNumber.Value}")
            : "—";

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
                PipelineRunResult.Succeeded => ("✓", StatusPanelRowEmphasis.Success, "Succeeded"),
                PipelineRunResult.PartiallySucceeded => ("!", StatusPanelRowEmphasis.Warning, "Partially succeeded"),
                PipelineRunResult.Failed => ("✕", StatusPanelRowEmphasis.Error, "Failed"),
                PipelineRunResult.Canceled => ("○", StatusPanelRowEmphasis.Normal, "Cancelled"),
                _ => ("○", StatusPanelRowEmphasis.Normal, "Completed")
            };
        }

        return ("○", StatusPanelRowEmphasis.Normal, "Unknown");
    }

    public static string? FormatTiming(AzurePipelineRunInfo run, DateTimeOffset utcNow)
    {
        if (!AzureRunSelector.IsActive(run.State))
        {
            return null;
        }

        var start = run.StartedAtUtc ?? run.QueuedAtUtc;
        return "Running " + FormatDuration(utcNow - start);
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

    private static bool ShouldShowAttentionAsRow(AzurePipelineRunInfo run) =>
        run.State == PipelineRunState.Completed
        && run.Result is PipelineRunResult.Failed or PipelineRunResult.PartiallySucceeded;

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

    private static AzureStatusPresentation Message(
        string glyph,
        string primary,
        string? secondary,
        StatusPanelRowEmphasis emphasis,
        string? attention = null) =>
        new(
            ShowSection: true,
            HeaderLabel: "AZURE DEVOPS",
            ShowTable: false,
            MessageGlyph: glyph,
            MessagePrimary: primary,
            MessageSecondary: secondary,
            Rows: [],
            AttentionLine: attention,
            PrimaryRunUrl: null,
            Emphasis: emphasis);

    private static AzureStatusPresentation Hidden() =>
        new(false, "AZURE DEVOPS", false, null, null, null, [], null, null, StatusPanelRowEmphasis.Normal);
}
