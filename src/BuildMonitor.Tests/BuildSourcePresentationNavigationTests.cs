using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

/// <summary>Azure BUILDS row semantic navigation (presentation layer).</summary>
public sealed class BuildSourcePresentationNavigationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 6, 0, 0, TimeSpan.Zero);
    private const string RunUrl = "https://dev.azure.com/org/project/_build/results?buildId=491&view=results";

    private static readonly AzureBuildNavigationContext NavContext = new(
        "p1",
        "conn",
        "https://dev.azure.com/org",
        "project",
        "repo");

    [Fact]
    public void Azure_primary_row_run_column_targets_PrimaryRun_build_results()
    {
        var primary = AzureRun(runId: 491, buildNumber: "20260828.3", pullRequestNumber: 185);
        var facet = Facet(primary);
        var row = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));
        var nav = row.AzureNavigation;
        Assert.NotNull(nav);

        Assert.Equal("#491", row.RunDisplay);
        Assert.Equal("20260828.3", row.BuildNumberDisplay);
        Assert.Equal("#185", row.PullRequestDisplay);
        Assert.Contains("buildId=491", nav.Run.Uri!, StringComparison.Ordinal);
        Assert.NotEqual(row.RunDisplay, row.BuildNumberDisplay);
    }

    [Fact]
    public void Azure_primary_uses_PrimaryRun_not_attention_or_previous_run()
    {
        var primary = AzureRun(runId: 491, buildNumber: "20260828.3", pullRequestNumber: 185);
        var attentionFailed = AzureRun(
            runId: 488,
            buildNumber: "20260828.1",
            pullRequestNumber: null,
            state: PipelineRunState.Completed,
            result: PipelineRunResult.Failed);
        var facet = Facet(primary, attentionFailed);
        var row = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));

        Assert.Equal("#491", row.RunDisplay);
        Assert.Contains("buildId=491", row.AzureNavigation!.Run.Uri!, StringComparison.Ordinal);
        Assert.DoesNotContain("488", row.AzureNavigation.Run.Uri!, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_status_invokes_lazy_failure_resolution()
    {
        var primary = AzureRun(
            runId: 491,
            buildNumber: "20260828.3",
            pullRequestNumber: 185,
            state: PipelineRunState.Completed,
            result: PipelineRunResult.Failed);
        var row = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(Facet(primary), true, true, Now));

        Assert.Equal(AzureBuildLinkKind.FailureDetails, row.AzureNavigation!.Status.Kind);
        Assert.NotNull(row.AzureNavigation.FailureRequest);
        Assert.Equal(491, row.AzureNavigation.FailureRequest!.RunId);
    }

    [Fact]
    public void Partial_status_invokes_lazy_failure_resolution()
    {
        var primary = AzureRun(
            runId: 491,
            buildNumber: "20260828.3",
            pullRequestNumber: null,
            state: PipelineRunState.Completed,
            result: PipelineRunResult.PartiallySucceeded);
        var row = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(Facet(primary), true, true, Now));

        Assert.Equal(AzureBuildLinkKind.FailureDetails, row.AzureNavigation!.Status.Kind);
    }

    [Fact]
    public void Succeeded_and_building_do_not_request_timeline_at_presentation_layer()
    {
        var succeeded = AzureRun(
            runId: 491,
            buildNumber: "20260828.3",
            pullRequestNumber: null,
            state: PipelineRunState.Completed,
            result: PipelineRunResult.Succeeded);
        var building = AzureRun(
            runId: 492,
            buildNumber: "20260828.4",
            pullRequestNumber: null,
            state: PipelineRunState.InProgress,
            result: PipelineRunResult.Unknown);

        var succeededNav = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(Facet(succeeded), true, true, Now)).AzureNavigation!;
        var buildingNav = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(Facet(building), true, true, Now)).AzureNavigation!;

        Assert.Equal(AzureBuildLinkKind.RunResults, succeededNav.Status.Kind);
        Assert.Null(succeededNav.FailureRequest);
        Assert.Equal(AzureBuildLinkKind.RunResults, buildingNav.Status.Kind);
        Assert.Null(buildingNav.FailureRequest);
    }

    [Fact]
    public void Azure_non_pr_run_has_dash_pull_request_display_and_run_results_link()
    {
        var primary = AzureRun(runId: 452, buildNumber: "20260825.13", pullRequestNumber: null, branch: "master", sourceBranchRef: "refs/heads/master");
        var facet = Facet(primary);
        var row = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));

        Assert.Equal("—", row.PullRequestDisplay);
        Assert.Contains("buildId=452", row.AzureNavigation!.Run.Uri!, StringComparison.Ordinal);
        Assert.Equal(AzureBuildLinkKind.None, row.AzureNavigation.PullRequest.Kind);
    }

    [Fact]
    public void Azure_message_mode_has_no_fabricated_navigation()
    {
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.AuthRequired,
            AzureCiMonitoringState.NotMonitored,
            FocusBranch: "master",
            PrimaryRun: null,
            AttentionRuns: [],
            PolledAtUtc: Now,
            HasSelectedPipelines: true,
            StatusMessage: "Sign in required");

        var row = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));

        Assert.Null(row.AzureNavigation);
        Assert.Equal("—", row.RunDisplay);
        Assert.Equal("—", row.PullRequestDisplay);
    }

    [Fact]
    public void Local_row_has_no_azure_navigation()
    {
        var snapshot = new ProjectHealthSnapshot(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: ProjectLifecycleState.Watching,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(4),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-1),
            IsActive: true,
            ProgressSteps: [],
            LastBuildExitCode: 0,
            Azure: Facet(AzureRun(491, "20260828.3", 185)));
        var controlPlane = ControlPlaneStatusFormatter.Format(snapshot, Now);
        var local = BuildSourcePresentationBuilder.TryBuildLocal(snapshot, controlPlane, Now);

        Assert.NotNull(local);
        Assert.Null(local!.AzureNavigation);
        Assert.Equal("—", local.RunDisplay);
        Assert.Equal("—", local.PullRequestDisplay);
    }

    [Fact]
    public void Deep_link_builder_uses_build_id_not_build_number()
    {
        var url = AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(
            "https://dev.azure.com/org",
            "project",
            491);

        Assert.Contains("buildId=491", url, StringComparison.Ordinal);
        Assert.DoesNotContain("20260828", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_table_row_run_url_matches_primary_run()
    {
        var run = AzureRun(491, "20260828.3", 185);
        var tableRow = AzureStatusPresentationBuilder.ToTableRow(run, Now);

        Assert.Equal("#491", tableRow.RunDisplay);
        Assert.Equal(RunUrl, tableRow.RunUrl);
        Assert.Equal("#185", tableRow.PullRequestDisplay);
    }

    private static AzurePipelineRunInfo AzureRun(
        long runId,
        string buildNumber,
        int? pullRequestNumber,
        string branch = "PR #185",
        string? sourceBranchRef = null,
        PipelineRunState state = PipelineRunState.InProgress,
        PipelineRunResult? result = null) =>
        new(
            DefinitionId: 8,
            PipelineDisplayName: "WitherbyConnect",
            RunId: runId,
            BuildNumber: buildNumber,
            State: state,
            Result: result ?? PipelineRunResult.Unknown,
            Branch: branch,
            QueuedAtUtc: Now.AddMinutes(-5),
            StartedAtUtc: Now.AddMinutes(-4),
            FinishedAtUtc: null,
            RunUrl: $"https://dev.azure.com/org/project/_build/results?buildId={runId}&view=results",
            PullRequestNumber: pullRequestNumber,
            SourceBranchRef: sourceBranchRef);

    private static ProjectAzureHealthFacet Facet(
        AzurePipelineRunInfo primary,
        params AzurePipelineRunInfo[] attention) =>
        new(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            FocusBranch: "master",
            primary,
            attention,
            Now,
            HasSelectedPipelines: true,
            NavigationContext: NavContext);
}
