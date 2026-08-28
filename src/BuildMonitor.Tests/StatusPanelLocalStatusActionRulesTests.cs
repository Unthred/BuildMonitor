using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelLocalStatusActionRulesTests
{
    [Theory]
    [InlineData(StatusPanelRowEmphasis.Warning, LocalBuildStatusLogAction.OpenLogWithWarningsFilter)]
    [InlineData(StatusPanelRowEmphasis.Error, LocalBuildStatusLogAction.OpenLogWithErrorsFilter)]
    [InlineData(StatusPanelRowEmphasis.Success, LocalBuildStatusLogAction.None)]
    [InlineData(StatusPanelRowEmphasis.Busy, LocalBuildStatusLogAction.None)]
    public void Local_status_click_action(StatusPanelRowEmphasis emphasis, LocalBuildStatusLogAction expected)
    {
        var row = LocalRow(emphasis);
        Assert.Equal(expected, StatusPanelLocalStatusActionRules.Resolve(row));
    }

    [Fact]
    public void Azure_status_never_opens_local_log()
    {
        var row = LocalRow(StatusPanelRowEmphasis.Warning) with { Source = "Azure" };
        Assert.Equal(LocalBuildStatusLogAction.None, StatusPanelLocalStatusActionRules.Resolve(row));
    }

    private static BuildSourcePresentationRow LocalRow(StatusPanelRowEmphasis emphasis) =>
        new(
            Source: "Local",
            StatusGlyph: "⚠",
            StatusText: "Warnings",
            BranchDisplay: "main",
            RunDisplay: "—",
            BuildNumberDisplay: "—",
            PullRequestDisplay: "—",
            AgeDisplay: "1m",
            IssuesDisplay: "0 / 3",
            AzureNavigation: null,
            Emphasis: emphasis);
}
