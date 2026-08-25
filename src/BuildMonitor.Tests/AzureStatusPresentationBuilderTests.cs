using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class AzureStatusPresentationBuilderTests
{
    [Fact]
    public void Zero_pipelines_shows_not_monitored_message_not_table()
    {
        var facet = AzureFacetComposer.NotMonitored(DateTimeOffset.UtcNow);
        var view = AzureStatusPresentationBuilder.Build(facet, true, hasSelectedPipelines: false, DateTimeOffset.UtcNow);
        Assert.True(view.ShowSection);
        Assert.False(view.ShowTable);
        Assert.Contains("Not monitored", view.MessagePrimary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Auth_required_shows_warning_message()
    {
        var facet = AzureFacetComposer.AuthRequired(DateTimeOffset.UtcNow, null, "Authentication required");
        var view = AzureStatusPresentationBuilder.Build(facet, true, true, DateTimeOffset.UtcNow);
        Assert.False(view.ShowTable);
        Assert.Equal("!", view.MessageGlyph);
        Assert.Equal(StatusPanelRowEmphasis.Warning, view.Emphasis);
    }

    [Fact]
    public void Table_row_keeps_RunId_and_BuildNumber_distinct()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            452,
            "20260825.13",
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded,
            "master",
            now.AddMinutes(-10),
            now.AddMinutes(-10),
            now,
            "https://dev.azure.com/org/proj/_build/results?buildId=452&view=results");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Healthy,
            "master",
            run,
            [],
            now);

        var view = AzureStatusPresentationBuilder.Build(facet, true, true, now);
        Assert.True(view.ShowTable);
        var row = Assert.Single(view.Rows);
        Assert.Equal("WitherbyConnect", row.Pipeline);
        Assert.Equal("✓", row.StatusGlyph);
        Assert.Equal("Succeeded", row.StatusText);
        Assert.Equal("master", row.Branch);
        Assert.Equal("#452", row.RunDisplay);
        Assert.Equal("20260825.13", row.BuildNumberDisplay);
        Assert.Equal("—", row.PullRequestDisplay);
        Assert.Contains("buildId=452", row.RunUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("20260825.13", row.RunUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_BuildNumber_does_not_substitute_RunId()
    {
        Assert.Equal("—", AzureStatusPresentationBuilder.FormatBuildNumber(null));
        Assert.Equal("—", AzureStatusPresentationBuilder.FormatBuildNumber(""));
        Assert.Equal("#452", AzureStatusPresentationBuilder.FormatRunId(452));
    }

    [Fact]
    public void PullRequest_display_formats_or_dash()
    {
        Assert.Equal("#327", AzureStatusPresentationBuilder.FormatPullRequest(327));
        Assert.Equal("—", AzureStatusPresentationBuilder.FormatPullRequest(null));
    }

    [Fact]
    public void Building_row_includes_timing_under_status()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            453,
            "20260825.14",
            PipelineRunState.InProgress,
            PipelineRunResult.Unknown,
            "feature/foo",
            now.AddMinutes(-2),
            now.AddMinutes(-2),
            null,
            "https://example/453",
            PullRequestNumber: 327);
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            "feature/foo",
            run,
            [],
            now);

        var view = AzureStatusPresentationBuilder.Build(facet, true, true, now);
        var row = Assert.Single(view.Rows);
        Assert.Equal("◉", row.StatusGlyph);
        Assert.Equal("Building", row.StatusText);
        Assert.Equal("#453", row.RunDisplay);
        Assert.Equal("20260825.14", row.BuildNumberDisplay);
        Assert.Equal("#327", row.PullRequestDisplay);
        Assert.StartsWith("Running ", row.TimingText, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_attention_appears_as_second_table_row()
    {
        var now = DateTimeOffset.UtcNow;
        var primary = new AzurePipelineRunInfo(
            1, "CI", 453, "14", PipelineRunState.InProgress, PipelineRunResult.Unknown, "feature/foo",
            now, now, null, "https://example/453");
        var other = new AzurePipelineRunInfo(
            2, "Security", 451, "12", PipelineRunState.Completed, PipelineRunResult.Failed, "master",
            now, now, now, "https://example/451");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "feature/foo",
            primary,
            [other],
            now);

        var view = AzureStatusPresentationBuilder.Build(facet, true, true, now);
        Assert.Equal(2, view.Rows.Count);
        Assert.Equal("CI", view.Rows[0].Pipeline);
        Assert.Equal("Security", view.Rows[1].Pipeline);
        Assert.Equal("✕", view.Rows[1].StatusGlyph);
        Assert.Null(view.AttentionLine);
    }

    [Fact]
    public void FormatDuration_minutes_and_seconds()
    {
        Assert.Equal("2m 14s", AzureStatusPresentationBuilder.FormatDuration(TimeSpan.FromSeconds(134)));
    }
}
