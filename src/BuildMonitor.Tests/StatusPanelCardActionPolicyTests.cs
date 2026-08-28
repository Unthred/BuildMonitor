using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

/// <summary>Project-scoped status card actions remain independent and presentation-stable.</summary>
public sealed class StatusPanelCardActionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Card_actions_are_enabled_for_active_local_project()
    {
        var card = BuildCard("project-a", supportsRestart: true);
        Assert.True(card.ShowRestartButtons);
        Assert.True(card.ShowRunTestsButton);
        Assert.Equal("project-a", card.ProjectId);
    }

    [Fact]
    public void Multiple_cards_keep_distinct_project_ids()
    {
        var presentation = StatusPanelPresentationBuilder.Build(
            [
                Snapshot("project-a", "A"),
                Snapshot("project-b", "B")
            ],
            null,
            Now);

        Assert.Equal(["project-a", "project-b"], presentation.Cards.Select(c => c.ProjectId).ToArray());
    }

    [Fact]
    public void Local_warnings_row_requests_log_open_not_azure_navigation()
    {
        var snapshot = Snapshot("project-a", "A") with
        {
            Health = MonitorHealth.Amber,
            HealthLabel = "Warnings",
            WarningCount = 2,
            ErrorCount = 0,
            State = ProjectLifecycleState.Watching
        };

        var row = Assert.Single(
            StatusPanelPresentationBuilder.Build([snapshot], null, Now).Cards[0].BuildSourceRows!,
            r => r.Source == "Local");

        Assert.Null(row.AzureNavigation);
        Assert.Equal(LocalBuildStatusLogAction.OpenLogWithWarningsFilter, StatusPanelLocalStatusActionRules.Resolve(row));
    }

    private static StatusPanelCardPresentation BuildCard(string projectId, bool supportsRestart)
    {
        var snapshot = Snapshot(projectId, projectId) with { SupportsAppRestart = supportsRestart };
        return StatusPanelPresentationBuilder.Build([snapshot], null, Now).Cards[0];
    }

    private static ProjectHealthSnapshot Snapshot(string projectId, string displayName) =>
        new(
            ProjectId: projectId,
            DisplayName: displayName,
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: ProjectLifecycleState.Watching,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(10),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-1),
            IsActive: true,
            ProgressSteps: [],
            SupportsAppRestart: true,
            ListenUrl: "http://localhost:5000");
}
