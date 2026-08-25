using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class AzureStatusPresentationBuilderTests
{
    [Fact]
    public void Zero_pipelines_shows_not_monitored()
    {
        var facet = AzureFacetComposer.NotMonitored(DateTimeOffset.UtcNow);
        var view = AzureStatusPresentationBuilder.Build(facet, true, hasSelectedPipelines: false, DateTimeOffset.UtcNow);
        Assert.True(view.ShowSection);
        Assert.Contains("Not monitored", view.PrimaryLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Auth_required_shows_warning()
    {
        var facet = AzureFacetComposer.AuthRequired(DateTimeOffset.UtcNow, null, "Authentication required");
        var view = AzureStatusPresentationBuilder.Build(facet, true, true, DateTimeOffset.UtcNow);
        Assert.Equal("!", view.Glyph);
        Assert.Equal(StatusPanelRowEmphasis.Warning, view.Emphasis);
        Assert.Contains("Authentication", view.PrimaryLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Building_run_formats_duration_and_branch()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            1842,
            "1842",
            PipelineRunState.InProgress,
            PipelineRunResult.Unknown,
            "master",
            now.AddMinutes(-2),
            now.AddMinutes(-2),
            null,
            "https://dev.azure.com/org/proj/_build/results?buildId=1842");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            "master",
            run,
            [],
            now);

        var view = AzureStatusPresentationBuilder.Build(facet, true, true, now);
        Assert.Equal("◉", view.Glyph);
        Assert.Equal("WitherbyConnect", view.PrimaryLine);
        Assert.Contains("Building", view.SecondaryLine, StringComparison.Ordinal);
        Assert.Contains("master", view.SecondaryLine, StringComparison.Ordinal);
        Assert.Equal(run.RunUrl, view.RunUrl);
    }

    [Fact]
    public void Attention_line_for_other_failed_pipeline()
    {
        var now = DateTimeOffset.UtcNow;
        var primary = new AzurePipelineRunInfo(
            1, "CI", 1, "1", PipelineRunState.InProgress, PipelineRunResult.Unknown, "feature/foo",
            now, now, null, "https://example/1");
        var other = new AzurePipelineRunInfo(
            2, "Nightly", 2, "2", PipelineRunState.Completed, PipelineRunResult.Failed, "master",
            now, now, now, "https://example/2");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "feature/foo",
            primary,
            [other],
            now);

        var view = AzureStatusPresentationBuilder.Build(facet, true, true, now);
        Assert.Contains("other pipeline failed", view.AttentionLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatDuration_minutes_and_seconds()
    {
        Assert.Equal("2m 14s", AzureStatusPresentationBuilder.FormatDuration(TimeSpan.FromSeconds(134)));
    }
}
