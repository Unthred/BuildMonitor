using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using System.Text.Json;

namespace BuildMonitor.Tests;

public sealed class BuildSourcePresentationBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Local_and_azure_share_row_shape_and_local_then_azure_order()
    {
        var azureRun = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            454,
            "20260825.15",
            PipelineRunState.Completed,
            PipelineRunResult.Failed,
            "PR #168",
            Now.AddMinutes(-10),
            Now.AddMinutes(-10),
            Now.AddMinutes(-5),
            "https://example/?buildId=454",
            168);
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            azureRun,
            [],
            Now,
            HasSelectedPipelines: true);
        var snapshot = BaseSnapshot() with
        {
            LastBuildExitCode = 0,
            Azure = facet,
            LocalGit = new LocalGitContext(LocalGitHeadStatus.Branch, "master", []),
            Health = MonitorHealth.Red,
            HealthLabel = "Failed"
        };
        var controlPlane = ControlPlaneStatusFormatter.Format(snapshot, Now);

        var rows = BuildSourcePresentationBuilder.BuildAll(snapshot, controlPlane, Now);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["Local", "Azure"], rows.Select(r => r.Source).ToArray());

        var local = rows[0];
        Assert.Equal("✓", local.StatusGlyph);
        Assert.Equal("Succeeded", local.StatusText);
        Assert.Equal("master", local.BranchDisplay);
        Assert.Equal("—", local.RunDisplay);
        Assert.Equal("—", local.BuildNumberDisplay);
        Assert.Equal("—", local.PullRequestDisplay);
        Assert.Equal("0E · 0W", local.IssuesDisplay);
        Assert.Null(local.DeepLinkUrl);
        Assert.Contains("·", local.AgeDisplay, StringComparison.Ordinal);

        var azure = rows[1];
        Assert.Equal("✕", azure.StatusGlyph);
        Assert.Equal("Failed", azure.StatusText);
        Assert.Equal("PR #168", azure.BranchDisplay);
        Assert.Equal("#454", azure.RunDisplay);
        Assert.Equal("20260825.15", azure.BuildNumberDisplay);
        Assert.Equal("#168", azure.PullRequestDisplay);
        Assert.Equal("—", azure.IssuesDisplay);
        Assert.Equal("https://example/?buildId=454", azure.DeepLinkUrl);
    }

    [Fact]
    public void Local_branch_comes_from_local_git_not_azure_focus_branch()
    {
        var azureRun = new AzurePipelineRunInfo(
            8,
            "Pipe",
            99,
            "1",
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded,
            "PR #168",
            Now.AddMinutes(-5),
            Now.AddMinutes(-5),
            Now.AddMinutes(-1),
            "https://example/?buildId=99",
            168);
        var snapshot = BaseSnapshot() with
        {
            LocalGit = new LocalGitContext(LocalGitHeadStatus.Branch, "feature/foo", []),
            Azure = new ProjectAzureHealthFacet(
                AzureMonitoringAvailability.Available,
                AzureCiMonitoringState.Healthy,
                FocusBranch: "master",
                azureRun,
                [],
                Now,
                HasSelectedPipelines: true)
        };
        var controlPlane = ControlPlaneStatusFormatter.Format(snapshot, Now);

        var rows = BuildSourcePresentationBuilder.BuildAll(snapshot, controlPlane, Now);

        Assert.Equal("feature/foo", rows[0].BranchDisplay);
        Assert.Equal("PR #168", rows[1].BranchDisplay);
        Assert.NotEqual(rows[0].BranchDisplay, rows[1].BranchDisplay);
    }

    [Fact]
    public void Local_normal_git_branch_shown_on_local_row()
    {
        var snapshot = BaseSnapshot() with
        {
            LocalGit = new LocalGitContext(LocalGitHeadStatus.Branch, "feature/foo", [])
        };
        var controlPlane = ControlPlaneStatusFormatter.Format(snapshot, Now);
        var local = BuildSourcePresentationBuilder.TryBuildLocal(snapshot, controlPlane, Now);

        Assert.NotNull(local);
        Assert.Equal("feature/foo", local!.BranchDisplay);
    }

    [Fact]
    public void Local_detached_git_shows_concise_detached()
    {
        Assert.Equal(
            "detached",
            BuildSourcePresentationBuilder.FormatLocalBranchDisplay(
                new LocalGitContext(LocalGitHeadStatus.Detached, null, [], "Detached HEAD")));
    }

    [Fact]
    public void Local_unavailable_git_shows_em_dash()
    {
        Assert.Equal(
            "—",
            BuildSourcePresentationBuilder.FormatLocalBranchDisplay(
                new LocalGitContext(LocalGitHeadStatus.Unavailable, null, [], "missing")));
        Assert.Equal("—", BuildSourcePresentationBuilder.FormatLocalBranchDisplay(null));
    }

    [Fact]
    public void Local_without_local_git_shows_em_dash_even_when_azure_focus_present()
    {
        var snapshot = BaseSnapshot() with
        {
            LocalGit = null,
            Azure = new ProjectAzureHealthFacet(
                AzureMonitoringAvailability.Available,
                AzureCiMonitoringState.Healthy,
                FocusBranch: "master",
                PrimaryRun: null,
                AttentionRuns: [],
                PolledAtUtc: Now,
                HasSelectedPipelines: true)
        };
        var controlPlane = ControlPlaneStatusFormatter.Format(snapshot, Now);
        var local = BuildSourcePresentationBuilder.TryBuildLocal(snapshot, controlPlane, Now);

        Assert.NotNull(local);
        Assert.Equal("—", local!.BranchDisplay);
    }

    [Fact]
    public void Azure_only_row_unaffected_when_local_git_absent()
    {
        var azureRun = new AzurePipelineRunInfo(
            8,
            "Pipe",
            454,
            "20260825.15",
            PipelineRunState.Completed,
            PipelineRunResult.Failed,
            "PR #168",
            Now.AddMinutes(-10),
            Now.AddMinutes(-10),
            Now.AddMinutes(-5),
            "https://example/?buildId=454",
            168);
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            FocusBranch: null,
            azureRun,
            [],
            Now,
            HasSelectedPipelines: true);

        var azure = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));
        Assert.Equal("PR #168", azure.BranchDisplay);
        Assert.Equal("#454", azure.RunDisplay);
    }

    [Fact]
    public void Settings_project_model_does_not_persist_live_local_git_branch()
    {
        var json = JsonSerializer.Serialize(new MonitoredProjectSettings
        {
            Id = "p1",
            DisplayName = "Demo",
            Local = new LocalProjectAttachment { RootFolder = @"C:\repo" }
        });

        Assert.DoesNotContain("CurrentBranch", json, StringComparison.Ordinal);
        Assert.DoesNotContain("feature/foo", json, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalGit", json, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusBranch", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Previous_failure_attention_not_shown_on_builds_row()
    {
        var failed = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            454,
            "20260825.15",
            PipelineRunState.Completed,
            PipelineRunResult.Failed,
            "PR #168",
            Now.AddMinutes(-10),
            Now.AddMinutes(-10),
            Now.AddMinutes(-5),
            "https://example/?buildId=454",
            168);
        var previous = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            453,
            "20260825.14",
            PipelineRunState.Completed,
            PipelineRunResult.Failed,
            "PR #168",
            Now.AddMinutes(-40),
            Now.AddMinutes(-40),
            Now.AddMinutes(-30),
            "https://example/?buildId=453",
            168);
        var building = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            466,
            "20260826.10",
            PipelineRunState.InProgress,
            PipelineRunResult.Unknown,
            "master",
            Now.AddMinutes(-2),
            Now.AddMinutes(-2),
            null,
            "https://example/?buildId=466");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            "master",
            building,
            [failed, previous],
            Now,
            HasSelectedPipelines: true);

        var rows = BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now);
        var azure = Assert.Single(rows);
        Assert.Equal("#466", azure.RunDisplay);
        Assert.Equal("20260826.10", azure.BuildNumberDisplay);
        Assert.Equal("https://example/?buildId=466", azure.DeepLinkUrl);
        Assert.Null(azure.AttentionNote);
        Assert.Equal(StatusPanelRowEmphasis.Busy, azure.Emphasis);
        Assert.Equal("Building", azure.StatusText);
    }

    [Fact]
    public void Azure_succeeded_uses_success_emphasis()
    {
        var run = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            460,
            "20260826.4",
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded,
            "master",
            Now.AddMinutes(-10),
            Now.AddMinutes(-10),
            Now.AddMinutes(-5),
            "https://example/?buildId=460");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Healthy,
            "master",
            run,
            [],
            Now,
            HasSelectedPipelines: true);

        var azure = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));
        Assert.Equal(StatusPanelRowEmphasis.Success, azure.Emphasis);
        Assert.Equal("Succeeded", azure.StatusText);
        Assert.Equal("✓", azure.StatusGlyph);
    }

    [Fact]
    public void Azure_failed_uses_error_emphasis()
    {
        var run = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            454,
            "20260825.15",
            PipelineRunState.Completed,
            PipelineRunResult.Failed,
            "PR #168",
            Now.AddMinutes(-10),
            Now.AddMinutes(-10),
            Now.AddMinutes(-5),
            "https://example/?buildId=454",
            168);
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            run,
            [],
            Now,
            HasSelectedPipelines: true);

        var azure = Assert.Single(BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now));
        Assert.Equal(StatusPanelRowEmphasis.Error, azure.Emphasis);
        Assert.Equal(168, run.PullRequestNumber);
        Assert.Equal("#454", azure.RunDisplay);
        Assert.Equal("20260825.15", azure.BuildNumberDisplay);
    }

    [Fact]
    public void Local_succeeded_uses_success_emphasis()
    {
        var snapshot = BaseSnapshot() with
        {
            State = ProjectLifecycleState.BuildOk,
            Health = MonitorHealth.Green,
            ErrorCount = 0,
            WarningCount = 0,
            LastBuildExitCode = 0
        };
        var row = BuildSourcePresentationBuilder.TryBuildLocal(
            snapshot,
            ControlPlaneStatusFormatter.Format(snapshot, Now),
            Now);
        Assert.NotNull(row);
        Assert.Equal(StatusPanelRowEmphasis.Success, row!.Emphasis);
        Assert.Equal("Succeeded", row.StatusText);
    }

    private static ProjectHealthSnapshot BaseSnapshot() =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: ProjectLifecycleState.Watching,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(4.3),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-1),
            IsActive: true,
            ProgressSteps: [],
            LastBuildExitCode: 0);
}
