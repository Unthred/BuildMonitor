using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;
using BuildMonitor.Infrastructure.Git;

namespace BuildMonitor.Tests;

public sealed class AzureActiveRunTimelineAttachTests
{
    [Fact]
    public async Task Settled_primary_run_does_not_fetch_timeline()
    {
        var timeline = new CountingTimelineClient();
        var service = CreateService(timeline);
        var facet = SettledFacet(runId: 10);

        var attached = await service.AttachActiveRunExecutionAsync(
            "p1",
            facet,
            "https://dev.azure.com/org",
            "proj",
            "pat",
            CancellationToken.None);

        Assert.Equal(0, timeline.CallCount);
        Assert.Null(attached.ExecutionDetail);
    }

    [Fact]
    public async Task Active_primary_run_fetches_timeline_once()
    {
        var stageId = Guid.NewGuid();
        var timeline = new CountingTimelineClient(OkTimeline(
            stageId,
            "Deploy Production",
            "inProgress"));
        var service = CreateService(timeline);
        var facet = ActiveFacet(runId: 42);

        var attached = await service.AttachActiveRunExecutionAsync(
            "p1",
            facet,
            "https://dev.azure.com/org",
            "proj",
            "pat",
            CancellationToken.None);

        Assert.Equal(1, timeline.CallCount);
        Assert.NotNull(attached.ExecutionDetail);
        Assert.Equal(42, attached.ExecutionDetail!.RunId);
        Assert.Equal(42, timeline.LastBuildId);
    }

    [Fact]
    public async Task Timeline_failure_falls_back_without_execution_detail()
    {
        var timeline = new CountingTimelineClient(
            new AzureBuildTimelineResult(AzureBuildTimelineOutcome.Unavailable, [], "boom"));
        var service = CreateService(timeline);
        var attached = await service.AttachActiveRunExecutionAsync(
            "p1",
            ActiveFacet(7),
            "https://dev.azure.com/org",
            "proj",
            "pat",
            CancellationToken.None);

        Assert.Equal(1, timeline.CallCount);
        Assert.Null(attached.ExecutionDetail);
        Assert.Equal(AzureCiMonitoringState.Activity, attached.CiState);
    }

    [Fact]
    public async Task RunId_change_clears_previous_execution_detail()
    {
        var stageA = Guid.NewGuid();
        var timeline = new CountingTimelineClient();
        timeline.SetResult(OkTimeline(stageA, "Stage A", "inProgress"));
        var service = CreateService(timeline);

        var first = await service.AttachActiveRunExecutionAsync(
            "p1", ActiveFacet(1), "https://dev.azure.com/org", "proj", "pat", CancellationToken.None);
        Assert.NotNull(first.ExecutionDetail);

        timeline.SetResult(OkTimeline(Guid.NewGuid(), "Stage B", "inProgress"));
        var second = await service.AttachActiveRunExecutionAsync(
            "p1", ActiveFacet(2), "https://dev.azure.com/org", "proj", "pat", CancellationToken.None);

        Assert.Equal(2, second.ExecutionDetail!.RunId);
        Assert.Contains(second.ExecutionDetail.Stages, s => s.Name == "Stage B");
        Assert.DoesNotContain(second.ExecutionDetail.Stages, s => s.Name == "Stage A");
    }

    [Fact]
    public async Task Timeline_response_for_superseded_RunId_is_discarded()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new DelayedTimelineClient(gate);
        delayed.Result = OkTimeline(Guid.NewGuid(), "Old Stage", "inProgress");
        var service = CreateService(delayed);

        var firstTask = service.AttachActiveRunExecutionAsync(
            "p1", ActiveFacet(100), "https://dev.azure.com/org", "proj", "pat", CancellationToken.None);

        await delayed.Entered.Task;
        delayed.Result = OkTimeline(Guid.NewGuid(), "New Stage", "inProgress");
        var second = await service.AttachActiveRunExecutionAsync(
            "p1", ActiveFacet(101), "https://dev.azure.com/org", "proj", "pat", CancellationToken.None);

        gate.SetResult();
        var first = await firstTask;

        Assert.Null(first.ExecutionDetail);
        Assert.Equal(101, second.ExecutionDetail!.RunId);
        Assert.Contains(second.ExecutionDetail.Stages, s => s.Name == "New Stage");
        Assert.DoesNotContain(second.ExecutionDetail.Stages, s => s.Name == "Old Stage");
    }

    [Fact]
    public async Task Settled_after_active_clears_execution_detail_without_second_fetch()
    {
        var timeline = new CountingTimelineClient();
        timeline.SetResult(OkTimeline(Guid.NewGuid(), "Deploy", "inProgress"));
        var service = CreateService(timeline);

        var active = await service.AttachActiveRunExecutionAsync(
            "p1", ActiveFacet(100), "https://dev.azure.com/org", "proj", "pat", CancellationToken.None);
        Assert.NotNull(active.ExecutionDetail);

        var settled = await service.AttachActiveRunExecutionAsync(
            "p1",
            SettledFacet(100),
            "https://dev.azure.com/org",
            "proj",
            "pat",
            CancellationToken.None);
        Assert.Null(settled.ExecutionDetail);
        Assert.Equal(1, timeline.CallCount);
    }

    [Fact]
    public async Task Projects_do_not_share_timeline_cache()
    {
        var timeline = new CountingTimelineClient();
        timeline.SetResult(OkTimeline(Guid.NewGuid(), "P1 Stage", "inProgress"));
        var service = CreateService(timeline);

        var a = await service.AttachActiveRunExecutionAsync(
            "project-a", ActiveFacet(1), "https://dev.azure.com/org", "proj", "pat", CancellationToken.None);
        timeline.SetResult(OkTimeline(Guid.NewGuid(), "P2 Stage", "inProgress"));
        var b = await service.AttachActiveRunExecutionAsync(
            "project-b", ActiveFacet(2), "https://dev.azure.com/org", "proj", "pat", CancellationToken.None);

        Assert.Equal("P1 Stage", a.ExecutionDetail!.Stages[0].Name);
        Assert.Equal("P2 Stage", b.ExecutionDetail!.Stages[0].Name);
    }

    [Fact]
    public void Monitoring_service_exposes_single_poll_loop_not_second_timer()
    {
        var fields = typeof(AzureMonitoringService)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(f => f.Name)
            .ToList();
        Assert.Contains("loopTask", fields);
        Assert.Contains("loopCts", fields);
        Assert.DoesNotContain(fields, n => n.Contains("timelineTimer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, n => n.Contains("secondTimer", StringComparison.OrdinalIgnoreCase));
    }

    private static AzureMonitoringService CreateService(IAzureBuildTimelineClient timeline) =>
        new(
            new FakePollClient(),
            new FakeSecretStore("pat"),
            new LocalGitContextReader(),
            () => { },
            timeline);

    private static ProjectAzureHealthFacet ActiveFacet(long runId) =>
        new(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            "main",
            new AzurePipelineRunInfo(
                1, "CI", runId, "1", PipelineRunState.InProgress, PipelineRunResult.Unknown,
                "main", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-1), null, "https://x"),
            [],
            DateTimeOffset.UtcNow);

    private static ProjectAzureHealthFacet SettledFacet(long runId) =>
        new(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Healthy,
            "main",
            new AzurePipelineRunInfo(
                1, "CI", runId, "1", PipelineRunState.Completed, PipelineRunResult.Succeeded,
                "main", DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-9),
                DateTimeOffset.UtcNow.AddMinutes(-1), "https://x"),
            [],
            DateTimeOffset.UtcNow);

    private static AzureBuildTimelineResult OkTimeline(Guid stageId, string name, string state) =>
        new(
            AzureBuildTimelineOutcome.Ok,
            [new AzureBuildTimelineRecord(stageId, null, "Stage", null, name, state, 1)],
            ChangeId: 9);

    private sealed class CountingTimelineClient : IAzureBuildTimelineClient
    {
        public int CallCount { get; private set; }
        public long LastBuildId { get; private set; }
        public AzureBuildTimelineResult Result { get; set; } =
            new(AzureBuildTimelineOutcome.Ok, [], ChangeId: 1);

        public CountingTimelineClient(AzureBuildTimelineResult? result = null)
        {
            if (result is not null)
            {
                Result = result;
            }
        }

        public void SetResult(AzureBuildTimelineResult result) => Result = result;

        public Task<AzureBuildTimelineResult> GetTimelineAsync(
            string organizationUrl,
            string adoProjectIdOrName,
            long buildId,
            string? pat,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastBuildId = buildId;
            return Task.FromResult(Result);
        }
    }

    private sealed class DelayedTimelineClient(TaskCompletionSource gate) : IAzureBuildTimelineClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AzureBuildTimelineResult Result { get; set; } =
            new(AzureBuildTimelineOutcome.Ok, [], ChangeId: 1);
        private int callCount;

        public async Task<AzureBuildTimelineResult> GetTimelineAsync(
            string organizationUrl,
            string adoProjectIdOrName,
            long buildId,
            string? pat,
            CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref callCount);
            var snapshot = Result;
            if (n == 1)
            {
                Entered.TrySetResult();
                await gate.Task.WaitAsync(cancellationToken);
                return snapshot;
            }

            return Result;
        }
    }

    private sealed class FakePollClient : IAzureBuildPollClient
    {
        public Task<AzureBuildPollResult> ListRecentBuildsAsync(
            string organizationUrl,
            string adoProjectIdOrName,
            int definitionId,
            string pipelineDisplayName,
            string? pat,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AzureBuildPollResult(AzureBuildPollOutcome.Ok, []));
    }

    private sealed class FakeSecretStore(string? pat) : IAzureConnectionSecretStore
    {
        public Task<string?> LoadAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.FromResult(pat);

        public Task SaveAsync(string connectionId, string patValue, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> ExistsAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.FromResult(!string.IsNullOrWhiteSpace(pat));
    }
}
