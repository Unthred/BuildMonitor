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

    [Fact]
    public void Map_settled_project_has_empty_activities_and_no_idle()
    {
        var info = ControlPlaneProjectStatusMapper.Map(
            "wc",
            "Demo",
            @"C:\src\Demo",
            "Demo.csproj",
            true,
            true,
            false,
            Snapshot(MonitorHealth.Green, ProjectLifecycleState.BuildOk, 0, 0, 0),
            session: null,
            DateTimeOffset.UtcNow);

        Assert.Empty(info.Activities);
        Assert.Null(info.ActivitySummary);
        Assert.DoesNotContain(info.Activities, a => a.Phase == ActivityPhaseKind.Idle);
    }

    [Fact]
    public void Map_null_snapshot_has_empty_activities()
    {
        var info = ControlPlaneProjectStatusMapper.Map(
            "wc",
            "Demo",
            @"C:\src\Demo",
            "Demo.csproj",
            true,
            true,
            false,
            snapshot: null,
            session: null,
            DateTimeOffset.UtcNow);

        Assert.Empty(info.Activities);
        Assert.Null(info.ActivitySummary);
    }

    [Fact]
    public void Map_local_building_exposes_one_local_build_activity()
    {
        var snap = ActivitySnapshot(
            ProjectLifecycleState.Building,
            progressSteps: [new BuildProgressStep("Restore packages", BuildStepStatus.Active)]);

        var info = MapWith(snap);

        Assert.Single(info.Activities);
        Assert.Equal(ActivitySourceKind.Local, info.Activities[0].Source);
        Assert.Equal(ActivityPhaseKind.Building, info.Activities[0].Phase);
        Assert.Equal("Restoring", info.Activities[0].Summary);
        Assert.Equal("Restoring", info.ActivitySummary);
        Assert.Null(info.Activities[0].Progress);
    }

    [Fact]
    public void Map_local_testing_current_only_progress_from_TestProgress_not_summary()
    {
        var started = DateTimeOffset.Parse("2026-09-10T08:59:00Z");
        var snap = ActivitySnapshot(
            ProjectLifecycleState.Testing,
            testProgress: new TestRunLiveProgress(318, started));

        var info = MapWith(snap);

        Assert.Single(info.Activities);
        Assert.Equal(ActivityPhaseKind.Testing, info.Activities[0].Phase);
        Assert.Equal("Running tests · 318 completed", info.Activities[0].Summary);
        Assert.Equal(started, info.Activities[0].StartedAtUtc);
        Assert.NotNull(info.Activities[0].Progress);
        Assert.Equal(318, info.Activities[0].Progress!.Current);
        Assert.Null(info.Activities[0].Progress.Total);
    }

    [Fact]
    public void Map_local_testing_authoritative_total()
    {
        var snap = ActivitySnapshot(
            ProjectLifecycleState.Testing,
            testProgress: new TestRunLiveProgress(318, DateTimeOffset.UtcNow, Total: 940));

        var info = MapWith(snap);

        Assert.Equal(new ControlPlaneActivityProgressInfo(318, 940), info.Activities[0].Progress);
        Assert.Equal("Running tests · 318 / 940", info.ActivitySummary);
    }

    [Fact]
    public void Map_testing_without_counters_omits_progress()
    {
        var info = MapWith(ActivitySnapshot(ProjectLifecycleState.Testing));

        Assert.Equal("Running tests", info.Activities[0].Summary);
        Assert.Null(info.Activities[0].Progress);
    }

    [Fact]
    public void Map_azure_activity_exposes_azure_fields()
    {
        var info = MapWith(
            ActivitySnapshot(ProjectLifecycleState.Watching, azure: AzureActivityFacet()),
            azureAttached: true);

        Assert.Single(info.Activities);
        var a = info.Activities[0];
        Assert.Equal(ActivitySourceKind.Azure, a.Source);
        Assert.Equal(ActivityPhaseKind.AzureInProgress, a.Phase);
        Assert.Equal("CI Pipeline · in progress", a.Summary);
        Assert.Equal("552", a.OperationId);
        Assert.Equal(552, a.AzureRunId);
        Assert.Equal("552", a.AzureBuildNumber);
    }

    [Fact]
    public void Map_agent_ship_check_retains_agent_source()
    {
        var cp = ProjectControlPlaneSnapshot.Unused with
        {
            ShipCheckInProgress = true,
            ShipCheckPhase = ControlPlaneShipCheckPhase.Building
        };
        var info = MapWith(ActivitySnapshot(ProjectLifecycleState.Building, controlPlane: cp));

        Assert.Single(info.Activities);
        Assert.Equal(ActivitySourceKind.Agent, info.Activities[0].Source);
        Assert.Equal(ActivityPhaseKind.ShipCheck, info.Activities[0].Phase);
        Assert.Equal("Ship check — building", info.ActivitySummary);
    }

    [Fact]
    public void Map_local_and_azure_coexistence_preserves_builder_order_and_summary()
    {
        var snap = ActivitySnapshot(
            ProjectLifecycleState.Testing,
            testProgress: new TestRunLiveProgress(12, DateTimeOffset.UtcNow),
            azure: AzureActivityFacet());

        var expected = ProjectActivityBuilder.Build(snap, DateTimeOffset.Parse("2026-09-10T12:00:00Z"));
        var info = MapWith(snap, azureAttached: true, utcNow: DateTimeOffset.Parse("2026-09-10T12:00:00Z"));

        Assert.Equal(2, info.Activities.Count);
        Assert.Equal(ActivitySourceKind.Local, info.Activities[0].Source);
        Assert.Equal(ActivitySourceKind.Azure, info.Activities[1].Source);
        Assert.Equal(expected.PrimaryStatusText, info.ActivitySummary);
        Assert.Equal(
            expected.Activities.Where(a => a.IsActive).Select(a => a.Source).ToArray(),
            info.Activities.Select(a => a.Source).ToArray());
    }

    [Fact]
    public void Map_failed_health_can_coexist_with_current_activity()
    {
        var snap = ActivitySnapshot(
            ProjectLifecycleState.Building,
            health: MonitorHealth.Red,
            errorCount: 3,
            progressSteps: [new BuildProgressStep("App", BuildStepStatus.Active)]);

        var info = MapWith(snap);

        Assert.Equal(MonitorHealth.Red, info.OverallHealth);
        Assert.Single(info.Activities);
        Assert.Equal(ActivityPhaseKind.Building, info.Activities[0].Phase);
    }

    [Fact]
    public void Map_does_not_reconstruct_activity_from_failed_history_lifecycle()
    {
        var snap = ActivitySnapshot(ProjectLifecycleState.TestFailed, health: MonitorHealth.Red, errorCount: 2);
        var info = MapWith(snap);

        Assert.Empty(info.Activities);
        Assert.Equal(MonitorHealth.Red, info.OverallHealth);
        Assert.Equal(ProjectLifecycleState.TestFailed, info.Local!.LifecycleState);
    }

    [Fact]
    public void Map_does_not_leak_prior_run_total_without_live_TestProgress()
    {
        // Settled after a prior run — no TestProgress on snapshot.
        var info = MapWith(ActivitySnapshot(ProjectLifecycleState.BuildOk));

        Assert.Empty(info.Activities);
        Assert.All(info.Activities, a => Assert.Null(a.Progress));
    }

    [Fact]
    public void Serialized_activities_are_additive_omit_nulls_and_forbid_v1_extras()
    {
        var snap = ActivitySnapshot(
            ProjectLifecycleState.Testing,
            testProgress: new TestRunLiveProgress(318, DateTimeOffset.Parse("2026-09-10T08:59:00Z")));

        var info = MapWith(snap);
        var json = JsonSerializer.Serialize(info, JsonOptions);

        Assert.Contains("\"activities\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"activitySummary\":", json, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"local\"", json, StringComparison.Ordinal);
        Assert.Contains("\"phase\":\"testing\"", json, StringComparison.Ordinal);
        Assert.Contains("\"progress\":{\"current\":318}", json, StringComparison.Ordinal);
        Assert.Contains("\"lifecycleState\":\"testing\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("updatedAtUtc", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("activityCoexistenceSummary", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"total\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"fraction\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"eta\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"percent\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialized_settled_always_includes_empty_activities_array()
    {
        var info = MapWith(ActivitySnapshot(ProjectLifecycleState.BuildOk));
        var json = JsonSerializer.Serialize(info, JsonOptions);

        Assert.Contains("\"activities\":[]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("activitySummary", json, StringComparison.Ordinal);
    }

    private static ControlPlaneProjectInfo MapWith(
        ProjectHealthSnapshot snap,
        bool azureAttached = false,
        DateTimeOffset? utcNow = null) =>
        ControlPlaneProjectStatusMapper.Map(
            snap.ProjectId,
            snap.DisplayName,
            @"C:\src\Demo",
            "Demo.csproj",
            isActiveInSession: true,
            hasLocal: true,
            azureAttached,
            snap,
            session: null,
            utcNow ?? DateTimeOffset.UtcNow);

    private static ProjectHealthSnapshot ActivitySnapshot(
        ProjectLifecycleState state,
        MonitorHealth health = MonitorHealth.Green,
        int errorCount = 0,
        IReadOnlyList<BuildProgressStep>? progressSteps = null,
        ProjectAzureHealthFacet? azure = null,
        ProjectControlPlaneSnapshot? controlPlane = null,
        TestRunLiveProgress? testProgress = null) =>
        new(
            "p1",
            "Demo",
            health,
            ProjectHealthEvaluator.ToLabel(health),
            state,
            0,
            null,
            null,
            errorCount,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            progressSteps ?? [],
            ControlPlane: controlPlane,
            Azure: azure,
            TestProgress: testProgress,
            LocalGit: new LocalGitContext(LocalGitHeadStatus.Branch, "master", []));

    private static ProjectAzureHealthFacet AzureActivityFacet() =>
        new(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            "refs/heads/main",
            new AzurePipelineRunInfo(
                1,
                "CI Pipeline",
                552,
                "552",
                PipelineRunState.InProgress,
                PipelineRunResult.Unknown,
                "refs/heads/main",
                DateTimeOffset.Parse("2026-09-10T11:00:00Z"),
                DateTimeOffset.Parse("2026-09-10T11:01:00Z"),
                null,
                "https://example/run/552"),
            [],
            DateTimeOffset.Parse("2026-09-10T12:00:00Z"));

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
