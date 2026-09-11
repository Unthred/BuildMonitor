using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.AzureDevOps;

namespace BuildMonitor.Tests;

public sealed class AzureRunExecutionProjectorTests
{
    [Fact]
    public void Queued_timeline_without_active_stage_falls_back_to_null_presentation()
    {
        var timeline = Timeline(
            Stage(Guid.NewGuid(), "Build", "pending", order: 1),
            Stage(Guid.NewGuid(), "Deploy", "pending", order: 2));

        var presentation = AzureRunExecutionProjector.Present(
            AzureRunExecutionProjector.TryCreateDetail(1, timeline)!);

        Assert.Null(presentation.Summary);
        Assert.Null(presentation.Detail);
        Assert.Null(presentation.Progress);
    }

    [Fact]
    public void Single_active_stage_and_job_maps_summary_and_detail()
    {
        var stageId = Guid.NewGuid();
        var timeline = Timeline(
            Stage(stageId, "Deploy Production", "inProgress", order: 3),
            Job(Guid.NewGuid(), stageId, "Deploy Web App", "inProgress", order: 1));

        var detail = AzureRunExecutionProjector.TryCreateDetail(100, timeline);
        var presentation = AzureRunExecutionProjector.Present(detail!);

        Assert.Equal("Deploy Production", presentation.Summary);
        Assert.Equal("Deploy Web App", presentation.Detail);
    }

    [Fact]
    public void Parallel_active_stages_use_concurrency_summary_without_progress()
    {
        var timeline = Timeline(
            Stage(Guid.NewGuid(), "A", "inProgress", order: 1),
            Stage(Guid.NewGuid(), "B", "inProgress", order: 2),
            Stage(Guid.NewGuid(), "C", "pending", order: 3));

        var presentation = AzureRunExecutionProjector.Present(
            AzureRunExecutionProjector.TryCreateDetail(1, timeline)!);

        Assert.Equal("2 stages running", presentation.Summary);
        Assert.Null(presentation.Progress);
        Assert.Null(presentation.ProgressCaption);
    }

    [Fact]
    public void Several_active_jobs_in_one_stage_do_not_pick_first_job()
    {
        var stageId = Guid.NewGuid();
        var timeline = Timeline(
            Stage(stageId, "Deploy", "inProgress", order: 2),
            Job(Guid.NewGuid(), stageId, "Job A", "inProgress", order: 1),
            Job(Guid.NewGuid(), stageId, "Job B", "inProgress", order: 2),
            Job(Guid.NewGuid(), stageId, "Job C", "inProgress", order: 3));

        var presentation = AzureRunExecutionProjector.Present(
            AzureRunExecutionProjector.TryCreateDetail(1, timeline)!);

        Assert.Equal("Deploy", presentation.Summary);
        Assert.Equal("3 jobs running", presentation.Detail);
        Assert.DoesNotContain("Job A", presentation.Detail);
    }

    [Fact]
    public void Sequential_stages_derive_current_from_completed_plus_active_not_order()
    {
        var timeline = Timeline(
            Stage(Guid.NewGuid(), "Build", "completed", order: 10),
            Stage(Guid.NewGuid(), "Test", "completed", order: 20),
            Stage(Guid.NewGuid(), "Deploy Production", "inProgress", order: 30),
            Stage(Guid.NewGuid(), "Smoke", "pending", order: 40),
            Stage(Guid.NewGuid(), "Notify", "pending", order: 50));

        var progress = AzureRunExecutionProjector.TryCreateSequentialStageProgress(
            AzureRunExecutionProjector.TryCreateDetail(1, timeline)!.Stages);

        Assert.NotNull(progress);
        Assert.Equal(3, progress!.Current);
        Assert.Equal(5, progress.Total);
        Assert.NotEqual(30, progress.Current);
    }

    [Fact]
    public void Missing_order_on_any_stage_suppresses_progress()
    {
        var stages = new[]
        {
            new AzureTimelineStageInfo(Guid.NewGuid(), "A", "completed", null, 1, null, null),
            new AzureTimelineStageInfo(Guid.NewGuid(), "B", "inProgress", null, null, null, null),
            new AzureTimelineStageInfo(Guid.NewGuid(), "C", "pending", null, 3, null, null)
        };

        Assert.Null(AzureRunExecutionProjector.TryCreateSequentialStageProgress(stages));
    }

    [Fact]
    public void Parser_retains_state_order_times_and_changeId()
    {
        var stageId = Guid.NewGuid();
        var json =
            $$"""
            {
              "changeId": 42,
              "records": [
                {
                  "id": "{{stageId}}",
                  "parentId": null,
                  "type": "Stage",
                  "name": "Deploy",
                  "state": "inProgress",
                  "result": null,
                  "order": 2,
                  "startTime": "2026-09-11T06:00:00Z"
                }
              ]
            }
            """;

        var parsed = AzureBuildTimelineParser.Parse(json);
        Assert.Equal(AzureBuildTimelineOutcome.Ok, parsed.Outcome);
        Assert.Equal(42, parsed.ChangeId);
        Assert.Equal("inProgress", parsed.Records[0].State);
        Assert.Equal(2, parsed.Records[0].Order);
        Assert.Equal(DateTimeOffset.Parse("2026-09-11T06:00:00Z"), parsed.Records[0].StartedAtUtc);
    }

    [Fact]
    public void Activity_builder_uses_execution_detail_and_ignores_mismatched_runId()
    {
        var stageId = Guid.NewGuid();
        var detail = AzureRunExecutionProjector.TryCreateDetail(
            552,
            Timeline(Stage(stageId, "Deploy Production", "inProgress", 1),
                Job(Guid.NewGuid(), stageId, "Deploy Web App", "inProgress", 1)))!;

        var matched = ProjectActivityBuilder.Build(
            Snapshot(AzureActivityFacet(552, detail)),
            DateTimeOffset.UtcNow);
        Assert.Equal("Deploy Production", matched.PrimaryStatusText);
        Assert.Equal("Deploy Web App", matched.Primary!.Detail);

        var mismatched = ProjectActivityBuilder.Build(
            Snapshot(AzureActivityFacet(999, detail)),
            DateTimeOffset.UtcNow);
        Assert.Equal("CI Pipeline · in progress", mismatched.PrimaryStatusText);
    }

    [Fact]
    public void Control_plane_projects_mapper_receives_enriched_activity()
    {
        var stageId = Guid.NewGuid();
        var detail = AzureRunExecutionProjector.TryCreateDetail(
            552,
            Timeline(
                Stage(Guid.NewGuid(), "Build", "completed", 1),
                Stage(stageId, "Deploy Production", "inProgress", 2),
                Stage(Guid.NewGuid(), "Verify", "pending", 3),
                Job(Guid.NewGuid(), stageId, "Deploy Web App", "inProgress", 1)))!;

        var snap = Snapshot(AzureActivityFacet(552, detail));
        var info = ControlPlaneProjectStatusMapper.Map(
            "p1",
            "Demo",
            @"C:\src\Demo",
            "Demo.csproj",
            true,
            true,
            true,
            snap,
            null,
            DateTimeOffset.UtcNow);

        Assert.Contains(info.Activities, a => a.Source == ActivitySourceKind.Azure);
        var azure = info.Activities.Single(a => a.Source == ActivitySourceKind.Azure);
        Assert.Equal("Deploy Production", azure.Summary);
        Assert.Equal("Deploy Web App", azure.Detail);
        Assert.Equal(new ControlPlaneActivityProgressInfo(2, 3), azure.Progress);
    }

    private static ProjectHealthSnapshot Snapshot(ProjectAzureHealthFacet azure) =>
        new(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Watching,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            Azure: azure);

    private static ProjectAzureHealthFacet AzureActivityFacet(long runId, AzureRunExecutionDetail? detail) =>
        new(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            "refs/heads/main",
            new AzurePipelineRunInfo(
                1,
                "CI Pipeline",
                runId,
                runId.ToString(),
                PipelineRunState.InProgress,
                PipelineRunResult.Unknown,
                "refs/heads/main",
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(-4),
                null,
                "https://example/run"),
            [],
            DateTimeOffset.UtcNow,
            ExecutionDetail: detail);

    private static AzureBuildTimelineResult Timeline(params AzureBuildTimelineRecord[] records) =>
        new(AzureBuildTimelineOutcome.Ok, records, ChangeId: 1);

    private static AzureBuildTimelineRecord Stage(Guid id, string name, string state, int? order) =>
        new(id, null, "Stage", null, name, state, order);

    private static AzureBuildTimelineRecord Job(Guid id, Guid stageId, string name, string state, int? order) =>
        new(id, stageId, "Job", null, name, state, order);
}
