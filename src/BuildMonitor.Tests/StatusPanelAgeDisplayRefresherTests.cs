using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelAgeDisplayRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HasAgeDisplayChanges_true_when_only_age_ticks()
    {
        var previous = LocalBuildPresentation(Now);
        var localRow = Assert.Single(previous.Cards[0].BuildSourceRows!, r => r.Source == "Local");
        var agedRow = localRow with { AgeDisplay = "4m · 4.3s → 4m · 19s" };
        var current = previous with
        {
            Cards = [previous.Cards[0] with { BuildSourceRows = [agedRow] }]
        };

        Assert.False(StatusPanelPresentationChangeDetector.RequiresCardRebuild(previous, current));
        Assert.False(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
        Assert.True(StatusPanelAgeDisplayRefresher.HasAgeDisplayChanges(previous, current));
    }

    [Fact]
    public void HasAgeDisplayChanges_false_when_structural_row_changes()
    {
        var previous = LocalBuildPresentation(Now);
        var localRow = Assert.Single(previous.Cards[0].BuildSourceRows!, r => r.Source == "Local");
        var changedRow = localRow with { RunDisplay = "492", AgeDisplay = "1m · 2s" };
        var current = previous with
        {
            Cards = [previous.Cards[0] with { BuildSourceRows = [changedRow] }]
        };

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
        Assert.False(StatusPanelAgeDisplayRefresher.HasAgeDisplayChanges(previous, current));
    }

    [Fact]
    public void CollectAgeDisplays_maps_project_and_source()
    {
        var presentation = LocalBuildPresentation(Now);
        var ages = StatusPanelAgeDisplayRefresher.CollectAgeDisplays(presentation);

        var key = new StatusPanelAgeDisplayRefresher.AgeDisplayCellKey("p1", "Local");
        Assert.True(ages.ContainsKey(key));
        Assert.False(string.IsNullOrWhiteSpace(ages[key]));
    }

    private static StatusPanelPresentation LocalBuildPresentation(DateTimeOffset utcNow) =>
        StatusPanelPresentationBuilder.Build(
            [
                new ProjectHealthSnapshot(
                    ProjectId: "p1",
                    DisplayName: "Demo",
                    Health: MonitorHealth.Green,
                    HealthLabel: "Healthy",
                    State: ProjectLifecycleState.BuildOk,
                    LastExitCode: 0,
                    LastDuration: TimeSpan.FromSeconds(4.3),
                    LastErrorPreview: null,
                    ErrorCount: 0,
                    WarningCount: 2,
                    LastChangedUtc: utcNow.AddMinutes(-4),
                    LastBuildFinishedAtUtc: utcNow.AddMinutes(-4),
                    IsActive: true,
                    ProgressSteps: [])
            ],
            panelDismissAtUtc: null,
            utcNow);
}
