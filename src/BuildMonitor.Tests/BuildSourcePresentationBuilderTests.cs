using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

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
    public void Local_without_focus_branch_shows_em_dash()
    {
        var snapshot = BaseSnapshot();
        var controlPlane = ControlPlaneStatusFormatter.Format(snapshot, Now);
        var local = BuildSourcePresentationBuilder.TryBuildLocal(snapshot, controlPlane, Now);

        Assert.NotNull(local);
        Assert.Equal("—", local!.BranchDisplay);
    }

    [Fact]
    public void Previous_failure_attention_fits_azure_row_note()
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
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            failed,
            [previous],
            Now,
            HasSelectedPipelines: true);

        var rows = BuildSourcePresentationBuilder.BuildAzureRows(facet, true, true, Now);
        var azure = Assert.Single(rows);
        Assert.Equal("#454", azure.RunDisplay);
        Assert.NotNull(azure.AttentionNote);
        Assert.Contains("#453", azure.AttentionNote, StringComparison.Ordinal);
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
