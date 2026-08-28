using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.AzureDevOps;

namespace BuildMonitor.Tests;

/// <summary>Azure BUILDS row deep-link and column semantics (presentation layer).</summary>
public sealed class BuildSourcePresentationNavigationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 6, 0, 0, TimeSpan.Zero);
    private const string RunUrl = "https://dev.azure.com/org/project/_build/results?buildId=491&view=results";

    [Fact]
    public void Azure_primary_row_deep_link_targets_PrimaryRun_build_results()
    {
        var primary = AzureRun(runId: 491, buildNumber: "20260828.3", pullRequestNumber: 185);
        var facet = Facet(primary);
        var row = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));

        Assert.Equal("#491", row.RunDisplay);
        Assert.Equal("20260828.3", row.BuildNumberDisplay);
        Assert.Equal("#185", row.PullRequestDisplay);
        Assert.Equal(RunUrl, row.DeepLinkUrl);
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
        Assert.Equal(RunUrl, row.DeepLinkUrl);
        Assert.DoesNotContain("488", row.DeepLinkUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_non_pr_run_has_dash_pull_request_display_and_same_run_deep_link()
    {
        var primary = AzureRun(runId: 452, buildNumber: "20260825.13", pullRequestNumber: null, branch: "master");
        var facet = Facet(primary);
        var row = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));

        Assert.Equal("—", row.PullRequestDisplay);
        Assert.Contains("buildId=452", row.DeepLinkUrl!, StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_message_mode_has_no_fabricated_deep_link()
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

        Assert.Null(row.DeepLinkUrl);
        Assert.Equal("—", row.RunDisplay);
        Assert.Equal("—", row.PullRequestDisplay);
    }

    [Fact]
    public void Local_row_has_no_azure_deep_link()
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
        Assert.Null(local!.DeepLinkUrl);
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
            PullRequestNumber: pullRequestNumber);

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
            HasSelectedPipelines: true);
}
