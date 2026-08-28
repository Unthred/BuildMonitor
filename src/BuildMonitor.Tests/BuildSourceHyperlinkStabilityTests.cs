using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

/// <summary>Regression tests for #97/#98 BUILDS hyperlink stability during poll ticks.</summary>
public sealed class BuildSourceHyperlinkStabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly AzureBuildNavigationContext NavContext = new(
        "p1",
        "conn",
        "https://dev.azure.com/org",
        "project",
        "repo");

    [Fact]
    public void Urgent_rebuild_false_when_only_age_and_hidden_azure_timing_change()
    {
        var primary = ActiveRun();
        var previous = BuildPresentation(primary, Now);
        var current = BuildPresentation(primary, Now.AddSeconds(12));

        Assert.False(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
        Assert.False(StatusPanelPresentationChangeDetector.RequiresCardRebuild(previous, current));
    }

    [Fact]
    public void Urgent_rebuild_false_when_navigation_recreated_with_equivalent_semantics()
    {
        var primary = SucceededRun();
        var previous = BuildPresentation(primary, Now);
        var current = BuildPresentation(primary, Now.AddMinutes(1));

        var prevRow = AzureRow(previous);
        var currRow = AzureRow(current);
        Assert.NotSame(prevRow.AzureNavigation, currRow.AzureNavigation);

        Assert.False(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    [Fact]
    public void Urgent_rebuild_false_for_failure_details_semantic_target_across_rebuilds()
    {
        var primary = FailedRun();
        var previous = BuildPresentation(primary, Now);
        var current = BuildPresentation(primary, Now.AddMinutes(1));

        Assert.Equal(AzureBuildLinkKind.FailureDetails, AzureRow(previous).AzureNavigation!.Status.Kind);
        Assert.False(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    [Fact]
    public void Urgent_rebuild_true_when_primary_run_changes()
    {
        var previous = BuildPresentation(SucceededRun(runId: 491), Now);
        var current = BuildPresentation(SucceededRun(runId: 492), Now);

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    [Fact]
    public void Urgent_rebuild_true_when_pull_request_link_genuinely_disappears()
    {
        var withPr = BuildPresentation(SucceededRun(runId: 491, pullRequestNumber: 185), Now);
        var withoutPr = BuildPresentation(SucceededRun(runId: 491, pullRequestNumber: null, branch: "master", sourceBranchRef: "refs/heads/master"), Now);

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(withPr, withoutPr));
    }

    [Fact]
    public void Urgent_rebuild_true_when_branch_link_genuinely_disappears()
    {
        var withBranch = BuildPresentation(
            SucceededRun(runId: 491, branch: "feature/foo", sourceBranchRef: "refs/heads/feature/foo"),
            Now);
        var withoutBranch = BuildPresentation(
            SucceededRun(runId: 491, branch: "PR #185", sourceBranchRef: null, pullRequestNumber: 185),
            Now);

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(withBranch, withoutBranch));
    }

    [Fact]
    public void All_column_targets_remain_present_for_stable_primary_run()
    {
        var primary = SucceededRun(runId: 491, pullRequestNumber: 185, branch: "feature/foo", sourceBranchRef: "refs/heads/feature/foo");
        var first = AzureRow(BuildPresentation(primary, Now));
        var second = AzureRow(BuildPresentation(primary, Now.AddSeconds(30)));

        Assert.All(
            new[]
            {
                (Name: "Status", First: first.AzureNavigation!.Status, Second: second.AzureNavigation!.Status),
                (Name: "Run", First: first.AzureNavigation.Run, Second: second.AzureNavigation.Run),
                (Name: "BuildNumber", First: first.AzureNavigation.BuildNumber, Second: second.AzureNavigation.BuildNumber),
                (Name: "PullRequest", First: first.AzureNavigation.PullRequest, Second: second.AzureNavigation.PullRequest),
                (Name: "Branch", First: first.AzureNavigation.Branch, Second: second.AzureNavigation.Branch)
            },
            pair => Assert.True(
                AzureBuildSourceNavigationSemanticEqual.LinkTargetEqual(pair.First, pair.Second),
                pair.Name));
    }

    [Fact]
    public void Semantic_equal_treats_equivalent_failure_requests_as_stable()
    {
        var left = new AzureBuildFailureNavigationRequest("p1", "conn", "https://dev.azure.com/org", "project", 491);
        var right = new AzureBuildFailureNavigationRequest("p1", "conn", "https://dev.azure.com/org", "project", 491);

        Assert.True(AzureBuildSourceNavigationSemanticEqual.FailureRequestEqual(left, right));
    }

    [Fact]
    public void Age_display_only_change_does_not_trigger_urgent_rebuild_regression_93()
    {
        var now = new DateTimeOffset(2026, 8, 28, 6, 0, 0, TimeSpan.Zero);
        var completed = SucceededRun(runId: 491, finishedAtUtc: now.AddMinutes(-1));
        var previous = BuildPresentation(completed, now);
        var azureRow = AzureRow(previous);
        var agedRow = azureRow with { AgeDisplay = "42m · 5m0s" };
        var agedCard = previous.Cards[0] with { BuildSourceRows = new[] { agedRow } };
        var current = previous with { Cards = new[] { agedCard } };

        Assert.False(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    private static StatusPanelPresentation BuildPresentation(AzurePipelineRunInfo primary, DateTimeOffset utcNow)
    {
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureRunSelector.IsActive(primary.State) ? AzureCiMonitoringState.Activity : AzureCiMonitoringState.Healthy,
            "master",
            primary,
            [],
            utcNow,
            HasSelectedPipelines: true,
            NavigationContext: NavContext);

        return StatusPanelPresentationBuilder.Build(
            [
                new ProjectHealthSnapshot(
                    ProjectId: "p1",
                    DisplayName: "Demo",
                    Health: MonitorHealth.Green,
                    HealthLabel: "Healthy",
                    State: ProjectLifecycleState.Idle,
                    LastExitCode: 0,
                    LastDuration: null,
                    LastErrorPreview: null,
                    ErrorCount: 0,
                    WarningCount: 0,
                    LastChangedUtc: utcNow,
                    LastBuildFinishedAtUtc: null,
                    IsActive: true,
                    ProgressSteps: [],
                    Azure: facet)
            ],
            null,
            utcNow);
    }

    private static BuildSourcePresentationRow AzureRow(StatusPanelPresentation presentation) =>
        Assert.Single(presentation.Cards[0].BuildSourceRows!, row => row.Source == "Azure");

    private static AzurePipelineRunInfo ActiveRun() =>
        new(
            8,
            "WitherbyConnect",
            491,
            "20260828.3",
            PipelineRunState.InProgress,
            PipelineRunResult.Unknown,
            "feature/foo",
            Now.AddMinutes(-5),
            Now.AddMinutes(-4),
            null,
            "https://dev.azure.com/org/project/_build/results?buildId=491&view=results",
            185,
            "refs/heads/feature/foo");

    private static AzurePipelineRunInfo SucceededRun(
        long runId = 491,
        int? pullRequestNumber = 185,
        string branch = "feature/foo",
        string? sourceBranchRef = "refs/heads/feature/foo",
        DateTimeOffset? finishedAtUtc = null) =>
        new(
            8,
            "WitherbyConnect",
            runId,
            "20260828.3",
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded,
            branch,
            Now.AddMinutes(-10),
            Now.AddMinutes(-9),
            finishedAtUtc ?? Now.AddMinutes(-1),
            $"https://dev.azure.com/org/project/_build/results?buildId={runId}&view=results",
            pullRequestNumber,
            sourceBranchRef);

    private static AzurePipelineRunInfo FailedRun() =>
        new(
            8,
            "WitherbyConnect",
            491,
            "20260828.3",
            PipelineRunState.Completed,
            PipelineRunResult.Failed,
            "feature/foo",
            Now.AddMinutes(-10),
            Now.AddMinutes(-9),
            Now.AddMinutes(-1),
            "https://dev.azure.com/org/project/_build/results?buildId=491&view=results",
            185,
            "refs/heads/feature/foo");
}
