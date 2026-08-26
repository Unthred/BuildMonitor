using System.Text.Json;
using System.Text.Json.Serialization;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneProjectStatusMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void MapAzure_uses_facet_primary_run_not_attention_history()
    {
        var oldRun = Run(457, "20260825.1", pullRequestNumber: 167);
        var current = Run(458, "20260826.3", pullRequestNumber: 168);
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            FocusBranch: "refs/pull/168/merge",
            PrimaryRun: current,
            AttentionRuns: [oldRun],
            PolledAtUtc: DateTimeOffset.Parse("2026-08-26T07:00:00Z"),
            HasSelectedPipelines: true);

        var dto = ControlPlaneProjectStatusMapper.MapAzure(facet, DateTimeOffset.Parse("2026-08-26T07:00:05Z"));

        Assert.Equal(458, dto.RunId);
        Assert.Equal("20260826.3", dto.BuildNumber);
        Assert.Equal(168, dto.PullRequestNumber);
        Assert.NotEqual(457L, dto.RunId);
        Assert.Equal(5, dto.AgeSeconds);
        Assert.Equal(DateTimeOffset.Parse("2026-08-26T07:00:00Z"), dto.PolledAtUtc);
        Assert.Equal("1 other pipeline failed", dto.AttentionSummary);
        Assert.Equal("Failed", dto.Status);
        Assert.Equal("WitherbyConnect", dto.Pipeline);
    }

    [Fact]
    public void MapAzure_keeps_runId_and_buildNumber_distinct()
    {
        var facet = AvailableFacet(Run(
            runId: 458,
            buildNumber: "20260826.xx",
            pullRequestNumber: 168,
            branch: "PR #168"));

        var dto = ControlPlaneProjectStatusMapper.MapAzure(facet, DateTimeOffset.UtcNow);

        Assert.Equal(458L, dto.RunId);
        Assert.Equal("20260826.xx", dto.BuildNumber);
        Assert.NotEqual(dto.RunId?.ToString(), dto.BuildNumber);
        Assert.Equal(168, dto.PullRequestNumber);
        Assert.Contains("buildId=458", dto.RunUrl!, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_includes_local_azure_and_overall_health()
    {
        var localSnap = Snapshot(
            health: MonitorHealth.Green,
            state: ProjectLifecycleState.BuildOk,
            errors: 0,
            warnings: 0,
            lastBuildExitCode: 0);
        var azure = AvailableFacet(
            Run(458, "20260826.3", 168),
            ci: AzureCiMonitoringState.Failed);
        var merged = ProjectHealthComposer.WithAzure(localSnap, azure);

        var info = ControlPlaneProjectStatusMapper.Map(
            "wc",
            "WitherbyConnect (main)",
            @"C:\src\WitherbyConnectDotNet9",
            "WitherbyConnect.csproj",
            isActiveInSession: true,
            hasLocal: true,
            azureAttached: true,
            merged,
            new ControlPlaneSessionStatus(ControlPlaneSessionState.Busy, DateTimeOffset.UtcNow, true, true),
            DateTimeOffset.UtcNow);

        Assert.Equal(MonitorHealth.Red, info.OverallHealth);
        Assert.NotNull(info.Local);
        Assert.Equal(MonitorHealth.Green, info.Local!.Status);
        Assert.Equal("master", info.Local.Branch);
        Assert.NotNull(info.Azure);
        Assert.Equal(458, info.Azure!.RunId);
        Assert.Equal(AzureCiMonitoringState.Failed, info.Azure.CiState);
        Assert.Equal(ControlPlaneSessionState.Busy, info.SessionState);
    }

    [Fact]
    public void MapAzure_auth_required_is_honest_without_fake_green_run()
    {
        var facet = AzureFacetComposer.AuthRequired(
            DateTimeOffset.Parse("2026-08-26T07:00:00Z"),
            "main",
            "Authentication required");

        var dto = ControlPlaneProjectStatusMapper.MapAzure(facet, DateTimeOffset.UtcNow);

        Assert.Equal(AzureMonitoringAvailability.AuthRequired, dto.Availability);
        Assert.Null(dto.RunId);
        Assert.Null(dto.BuildNumber);
        Assert.Null(dto.RunUrl);
        Assert.Equal("Authentication required", dto.StatusMessage);
    }

    [Fact]
    public void MapAzure_unavailable_exposes_availability_and_polledAt()
    {
        var facet = AzureFacetComposer.Unavailable(
            DateTimeOffset.Parse("2026-08-26T06:59:00Z"),
            "main",
            "Azure DevOps unavailable");

        var dto = ControlPlaneProjectStatusMapper.MapAzure(
            facet,
            DateTimeOffset.Parse("2026-08-26T07:00:00Z"));

        Assert.Equal(AzureMonitoringAvailability.Unavailable, dto.Availability);
        Assert.Null(dto.RunId);
        Assert.Equal(DateTimeOffset.Parse("2026-08-26T06:59:00Z"), dto.PolledAtUtc);
        Assert.Equal(60, dto.AgeSeconds);
    }

    [Fact]
    public void MapAzure_zero_pipelines_has_no_fake_run()
    {
        var facet = AzureFacetComposer.NotMonitored(DateTimeOffset.UtcNow, "main");

        var dto = ControlPlaneProjectStatusMapper.MapAzure(facet, DateTimeOffset.UtcNow);

        Assert.Equal(AzureCiMonitoringState.NotMonitored, dto.CiState);
        Assert.False(dto.HasSelectedPipelines);
        Assert.Null(dto.RunId);
        Assert.Null(dto.BuildNumber);
        Assert.Null(dto.PullRequestNumber);
    }

    [Fact]
    public void Map_omits_azure_when_not_attached()
    {
        var info = ControlPlaneProjectStatusMapper.Map(
            "local-only",
            "Demo",
            @"C:\src\Demo",
            "Demo.csproj",
            true,
            hasLocal: true,
            azureAttached: false,
            Snapshot(MonitorHealth.Green, ProjectLifecycleState.BuildOk, 0, 0, 0),
            session: null,
            DateTimeOffset.UtcNow);

        Assert.Null(info.Azure);
        Assert.NotNull(info.Local);
    }

    [Fact]
    public void Serialized_projects_json_has_no_pat_or_token_fields()
    {
        var merged = ProjectHealthComposer.WithAzure(
            Snapshot(MonitorHealth.Green, ProjectLifecycleState.BuildOk, 0, 0, 0),
            AvailableFacet(Run(458, "20260826.3", 168)));

        var info = ControlPlaneProjectStatusMapper.Map(
            "wc",
            "WitherbyConnect (main)",
            @"C:\src\WC",
            "WC.csproj",
            true,
            true,
            true,
            merged,
            null,
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(new[] { info }, JsonOptions);

        Assert.DoesNotContain("pat", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"runId\":458", json, StringComparison.Ordinal);
        Assert.Contains("\"buildNumber\":\"20260826.3\"", json, StringComparison.Ordinal);
        Assert.Contains("\"pullRequestNumber\":168", json, StringComparison.Ordinal);
        Assert.Contains("\"polledAtUtc\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MapAzure_stale_attention_does_not_override_availability()
    {
        var lastKnown = Run(457, "20260825.1", 160);
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Unavailable,
            AzureCiMonitoringState.NotMonitored,
            FocusBranch: "main",
            PrimaryRun: null,
            AttentionRuns: [lastKnown],
            PolledAtUtc: DateTimeOffset.UtcNow,
            StatusMessage: "Azure DevOps unavailable",
            HasSelectedPipelines: true);

        var dto = ControlPlaneProjectStatusMapper.MapAzure(facet, DateTimeOffset.UtcNow);

        Assert.Equal(AzureMonitoringAvailability.Unavailable, dto.Availability);
        Assert.Null(dto.RunId);
        Assert.Contains("failed", dto.AttentionSummary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectAzureHealthFacet AvailableFacet(
        AzurePipelineRunInfo primary,
        AzureCiMonitoringState ci = AzureCiMonitoringState.Healthy) =>
        new(
            AzureMonitoringAvailability.Available,
            ci,
            FocusBranch: "master",
            PrimaryRun: primary,
            AttentionRuns: [],
            PolledAtUtc: DateTimeOffset.Parse("2026-08-26T07:00:00Z"),
            HasSelectedPipelines: true);

    private static AzurePipelineRunInfo Run(
        long runId,
        string buildNumber,
        int? pullRequestNumber = null,
        string branch = "PR #168") =>
        new(
            DefinitionId: 12,
            PipelineDisplayName: "WitherbyConnect",
            RunId: runId,
            BuildNumber: buildNumber,
            State: PipelineRunState.Completed,
            Result: PipelineRunResult.Failed,
            Branch: branch,
            QueuedAtUtc: DateTimeOffset.Parse("2026-08-26T06:50:00Z"),
            StartedAtUtc: DateTimeOffset.Parse("2026-08-26T06:50:01Z"),
            FinishedAtUtc: DateTimeOffset.Parse("2026-08-26T06:55:00Z"),
            RunUrl: $"https://dev.azure.com/org/proj/_build/results?buildId={runId}",
            PullRequestNumber: pullRequestNumber);

    private static ProjectHealthSnapshot Snapshot(
        MonitorHealth health,
        ProjectLifecycleState state,
        int errors,
        int warnings,
        int lastBuildExitCode) =>
        new(
            "wc",
            "WitherbyConnect (main)",
            health,
            ProjectHealthEvaluator.ToLabel(health),
            state,
            lastBuildExitCode,
            TimeSpan.FromMinutes(1),
            null,
            errors,
            warnings,
            DateTimeOffset.UtcNow,
            DateTimeOffset.Parse("2026-08-26T06:40:00Z"),
            true,
            [],
            LastBuildExitCode: lastBuildExitCode,
            LocalGit: new LocalGitContext(LocalGitHeadStatus.Branch, "master", []));
}
