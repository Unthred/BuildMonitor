using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.AzureDevOps;

namespace BuildMonitor.Tests;

public sealed class AzureBuildSourceNavigationBuilderTests
{
    private static readonly AzureBuildNavigationContext Context = new(
        "p1",
        "conn-1",
        "https://dev.azure.com/org",
        "My Project",
        "MyRepo");

    [Fact]
    public void Succeeded_status_uses_run_results_not_failure_resolution()
    {
        var run = Run(PipelineRunState.Completed, PipelineRunResult.Succeeded);
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(AzureBuildLinkKind.RunResults, nav.Status.Kind);
        Assert.Contains("buildId=491", nav.Status.Uri!, StringComparison.Ordinal);
        Assert.Null(nav.FailureRequest);
    }

    [Fact]
    public void Building_status_uses_run_results()
    {
        var run = Run(PipelineRunState.InProgress, PipelineRunResult.Unknown);
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(AzureBuildLinkKind.RunResults, nav.Status.Kind);
        Assert.Null(nav.FailureRequest);
    }

    [Fact]
    public void Failed_status_uses_lazy_failure_resolution()
    {
        var run = Run(PipelineRunState.Completed, PipelineRunResult.Failed);
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(AzureBuildLinkKind.FailureDetails, nav.Status.Kind);
        Assert.Null(nav.Status.Uri);
        Assert.NotNull(nav.FailureRequest);
        Assert.Equal(491, nav.FailureRequest!.RunId);
    }

    [Fact]
    public void Partial_status_uses_lazy_failure_resolution()
    {
        var run = Run(PipelineRunState.Completed, PipelineRunResult.PartiallySucceeded);
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(AzureBuildLinkKind.FailureDetails, nav.Status.Kind);
        Assert.NotNull(nav.FailureRequest);
    }

    [Fact]
    public void Run_and_build_number_target_same_build_results()
    {
        var run = Run(PipelineRunState.Completed, PipelineRunResult.Succeeded);
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(nav.Run.Uri, nav.BuildNumber.Uri);
        Assert.Contains("buildId=491", nav.Run.Uri!, StringComparison.Ordinal);
    }

    [Fact]
    public void Pull_request_link_when_trustworthy_pr_metadata_exists()
    {
        var run = Run(PipelineRunState.Completed, PipelineRunResult.Succeeded, pullRequestNumber: 185);
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(AzureBuildLinkKind.PullRequest, nav.PullRequest.Kind);
        Assert.Contains("/pullrequest/185", nav.PullRequest.Uri!, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_pr_run_has_no_pull_request_link()
    {
        var run = Run(PipelineRunState.Completed, PipelineRunResult.Succeeded, branch: "master", sourceBranchRef: "refs/heads/master");
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(AzureBuildLinkKind.None, nav.PullRequest.Kind);
    }

    [Fact]
    public void Branch_link_uses_real_source_branch_not_merge_ref()
    {
        var run = Run(
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded,
            branch: "feature/foo",
            sourceBranchRef: "refs/heads/feature/foo");
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(AzureBuildLinkKind.Branch, nav.Branch.Kind);
        Assert.Contains("version=GBfeature%2Ffoo", nav.Branch.Uri!, StringComparison.Ordinal);
    }

    [Fact]
    public void Pr_merge_ref_without_source_branch_has_no_branch_link()
    {
        var run = Run(
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded,
            branch: "PR #185",
            sourceBranchRef: null,
            pullRequestNumber: 185);
        var nav = AzureBuildSourceNavigationBuilder.Build(run, Context);

        Assert.Equal(AzureBuildLinkKind.None, nav.Branch.Kind);
    }

    private static AzurePipelineRunInfo Run(
        PipelineRunState state,
        PipelineRunResult result,
        string branch = "master",
        string? sourceBranchRef = "refs/heads/master",
        int? pullRequestNumber = null) =>
        new(
            8,
            "Pipe",
            491,
            "20260828.3",
            state,
            result,
            branch,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(-4),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "https://dev.azure.com/org/project/_build/results?buildId=491&view=results",
            pullRequestNumber,
            sourceBranchRef);
}

public sealed class AzureBuildTimelineFailureSelectorTests
{
    [Fact]
    public void Selects_failed_task_with_job_parent_for_logs_deep_link()
    {
        var jobId = Guid.Parse("899c4bff-9ac3-12de-4775-50e701812cb4");
        var taskId = Guid.Parse("bc949ec8-c945-5220-1d40-d8ea7dab4bda");
        var records = new List<AzureBuildTimelineRecord>
        {
            new(jobId, Guid.NewGuid(), "Job", "failed", "Build job"),
            new(taskId, jobId, "Task", "failed", "Compile")
        };

        var target = AzureBuildTimelineFailureSelector.TrySelectBestFailedLogTarget(records);

        Assert.NotNull(target);
        Assert.Equal(jobId, target!.JobId);
        Assert.Equal(taskId, target.TaskId);
    }

    [Fact]
    public void Falls_back_to_failed_job_when_no_failed_task()
    {
        var jobId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var records = new List<AzureBuildTimelineRecord>
        {
            new(jobId, null, "Job", "failed", "Build job")
        };

        var target = AzureBuildTimelineFailureSelector.TrySelectBestFailedLogTarget(records);

        Assert.NotNull(target);
        Assert.Equal(jobId, target!.JobId);
        Assert.Null(target.TaskId);
    }

    [Fact]
    public void No_usable_failed_record_returns_null()
    {
        var records = new List<AzureBuildTimelineRecord>
        {
            new(Guid.NewGuid(), null, "Stage", "succeeded", "Build")
        };

        Assert.Null(AzureBuildTimelineFailureSelector.TrySelectBestFailedLogTarget(records));
    }
}

public sealed class AzureDevOpsDeepLinkBuilderTests
{
    [Fact]
    public void Task_logs_url_uses_verified_view_logs_j_and_t_guids()
    {
        var jobId = Guid.Parse("899c4bff-9ac3-12de-4775-50e701812cb4");
        var taskId = Guid.Parse("bc949ec8-c945-5220-1d40-d8ea7dab4bda");
        var url = AzureDevOpsDeepLinkBuilder.BuildRunTaskLogsUrl(
            "https://dev.azure.com/myorg",
            "myspace",
            1234,
            jobId,
            taskId);

        Assert.Equal(
            "https://dev.azure.com/myorg/myspace/_build/results?buildId=1234&view=logs&j=899c4bff-9ac3-12de-4775-50e701812cb4&t=bc949ec8-c945-5220-1d40-d8ea7dab4bda",
            url);
    }
}

public sealed class AzureFailureNavigationResolverTests
{
    private static readonly AzureBuildFailureNavigationRequest Request = new(
        "p1",
        "conn",
        "https://dev.azure.com/org",
        "project",
        491);

    [Fact]
    public async Task Timeline_success_produces_failed_task_logs_url()
    {
        var jobId = Guid.Parse("899c4bff-9ac3-12de-4775-50e701812cb4");
        var taskId = Guid.Parse("bc949ec8-c945-5220-1d40-d8ea7dab4bda");
        var timeline = new AzureBuildTimelineResult(
            AzureBuildTimelineOutcome.Ok,
            [
                new AzureBuildTimelineRecord(jobId, Guid.NewGuid(), "Job", "failed", "Job"),
                new AzureBuildTimelineRecord(taskId, jobId, "Task", "failed", "Compile")
            ]);
        var timelineClient = new FakeTimelineClient(timeline);
        var secretStore = new FakeSecretStore("pat");
        var resolver = new AzureFailureNavigationResolver(timelineClient, secretStore);

        var uri = await resolver.ResolveAsync(Request, CancellationToken.None);

        Assert.Contains("view=logs", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains($"j={jobId:D}", uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"t={taskId:D}", uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pat", uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeline_failure_falls_back_to_build_results()
    {
        var timelineClient = new FakeTimelineClient(new AzureBuildTimelineResult(
            AzureBuildTimelineOutcome.Unavailable,
            [],
            "network"));
        var resolver = new AzureFailureNavigationResolver(timelineClient, new FakeSecretStore("pat"));

        var uri = await resolver.ResolveAsync(Request, CancellationToken.None);

        Assert.Contains("view=results", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("buildId=491", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_failed_timeline_record_falls_back_to_build_results()
    {
        var timelineClient = new FakeTimelineClient(new AzureBuildTimelineResult(
            AzureBuildTimelineOutcome.Ok,
            [new AzureBuildTimelineRecord(Guid.NewGuid(), null, "Stage", "succeeded", "Build")]));
        var resolver = new AzureFailureNavigationResolver(timelineClient, new FakeSecretStore("pat"));

        var uri = await resolver.ResolveAsync(Request, CancellationToken.None);

        Assert.Contains("view=results", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_resolve_uses_cache_without_second_timeline_call()
    {
        var timelineClient = new CountingTimelineClient(new AzureBuildTimelineResult(
            AzureBuildTimelineOutcome.Unavailable,
            []));
        var resolver = new AzureFailureNavigationResolver(timelineClient, new FakeSecretStore("pat"));

        _ = await resolver.ResolveAsync(Request, CancellationToken.None);
        _ = await resolver.ResolveAsync(Request, CancellationToken.None);

        Assert.Equal(1, timelineClient.CallCount);
        Assert.True(resolver.TryGetCached(Request, out var cached));
        Assert.NotNull(cached);
    }

    private sealed class FakeTimelineClient(AzureBuildTimelineResult result) : IAzureBuildTimelineClient
    {
        public Task<AzureBuildTimelineResult> GetTimelineAsync(
            string organizationUrl,
            string adoProjectIdOrName,
            long buildId,
            string? pat,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class CountingTimelineClient(AzureBuildTimelineResult result) : IAzureBuildTimelineClient
    {
        public int CallCount { get; private set; }

        public Task<AzureBuildTimelineResult> GetTimelineAsync(
            string organizationUrl,
            string adoProjectIdOrName,
            long buildId,
            string? pat,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeSecretStore(string pat) : IAzureConnectionSecretStore
    {
        public Task<string?> LoadAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(pat);

        public Task SaveAsync(string connectionId, string secret, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> ExistsAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}

public sealed class AzureMonitoringTimelinePollIsolationTests
{
    [Fact]
    public void Poll_client_does_not_fetch_timeline()
    {
        Assert.DoesNotContain(
            "timeline",
            typeof(AzureBuildPollClient).GetMethods()
                .SelectMany(m => m.GetParameters().Select(p => p.Name ?? string.Empty))
                .Concat(typeof(AzureMonitoringService).GetMethods().Select(m => m.Name)),
            StringComparer.OrdinalIgnoreCase);
    }
}
