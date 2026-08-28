using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelBuildSourceVolatileRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HasAgeOnlyChanges_true_when_only_age_ticks()
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
        Assert.True(StatusPanelBuildSourceVolatileRefresher.HasAgeOnlyChanges(previous, current));
        Assert.True(StatusPanelBuildSourceVolatileRefresher.HasVolatilePresentationChanges(previous, current));
    }

    [Fact]
    public void CollectVolatileRows_captures_building_status_and_in_progress_age()
    {
        var previous = LocalBuildPresentation(Now);
        var localRow = Assert.Single(previous.Cards[0].BuildSourceRows!, r => r.Source == "Local");
        var buildingRow = localRow with
        {
            StatusGlyph = "◉",
            StatusText = "Building",
            AgeDisplay = "In progress",
            Emphasis = StatusPanelRowEmphasis.Active
        };
        var current = previous with
        {
            Cards = [previous.Cards[0] with { BuildSourceRows = [buildingRow] }]
        };

        var key = new StatusPanelBuildSourceVolatileRefresher.BuildSourceCellKey("p1", "Local");
        var row = StatusPanelBuildSourceVolatileRefresher.CollectVolatileRows(current)[key];
        Assert.Equal("Building", row.StatusText);
        Assert.Equal("In progress", row.AgeDisplay);
        Assert.Equal(StatusPanelRowEmphasis.Active, row.Emphasis);
    }

    [Fact]
    public void HasAgeOnlyChanges_false_when_local_status_changes()
    {
        var previous = LocalBuildPresentation(Now);
        var localRow = Assert.Single(previous.Cards[0].BuildSourceRows!, r => r.Source == "Local");
        var buildingRow = localRow with
        {
            StatusGlyph = "◉",
            StatusText = "Building",
            AgeDisplay = "In progress",
            Emphasis = StatusPanelRowEmphasis.Active
        };
        var current = previous with
        {
            Cards = [previous.Cards[0] with { BuildSourceRows = [buildingRow] }]
        };

        Assert.False(StatusPanelBuildSourceVolatileRefresher.HasAgeOnlyChanges(previous, current));
    }

    [Fact]
    public void HasAgeOnlyChanges_false_when_structural_row_changes()
    {
        var previous = LocalBuildPresentation(Now);
        var localRow = Assert.Single(previous.Cards[0].BuildSourceRows!, r => r.Source == "Local");
        var changedRow = localRow with { RunDisplay = "492", AgeDisplay = "1m · 2s" };
        var current = previous with
        {
            Cards = [previous.Cards[0] with { BuildSourceRows = [changedRow] }]
        };

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
        Assert.False(StatusPanelBuildSourceVolatileRefresher.HasAgeOnlyChanges(previous, current));
    }

    [Fact]
    public void CollectVolatileRows_maps_project_source_and_status()
    {
        var presentation = LocalBuildPresentation(Now);
        var rows = StatusPanelBuildSourceVolatileRefresher.CollectVolatileRows(presentation);

        var key = new StatusPanelBuildSourceVolatileRefresher.BuildSourceCellKey("p1", "Local");
        Assert.True(rows.ContainsKey(key));
        Assert.False(string.IsNullOrWhiteSpace(rows[key].AgeDisplay));
        Assert.Equal("Warnings", rows[key].StatusText);
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
