using System.Net;
using System.Text;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Infrastructure.AzureDevOps;

namespace BuildMonitor.Tests;

public sealed class AzureBuildPollClientTests
{
    [Fact]
    public async Task Missing_pat_does_not_mock_success()
    {
        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            pat: null,
            CancellationToken.None);

        Assert.Equal(AzureBuildPollOutcome.PatMissing, result.Outcome);
        Assert.Empty(result.Runs);
    }

    [Fact]
    public async Task Unauthorized_maps_to_auth_required()
    {
        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "secret-pat",
            CancellationToken.None);

        Assert.Equal(AzureBuildPollOutcome.AuthRequired, result.Outcome);
    }

    [Fact]
    public async Task Ok_parses_distinct_run_id_build_number_and_pr()
    {
        const string json = """
            {
              "value": [
                {
                  "id": 452,
                  "buildNumber": "20260825.13",
                  "status": "completed",
                  "result": "succeeded",
                  "reason": "individualCI",
                  "sourceBranch": "refs/heads/master",
                  "sourceVersion": "691538acd954126677fabf666d8af886ad094a27",
                  "queueTime": "2026-08-25T09:24:15Z",
                  "startTime": "2026-08-25T09:24:25Z",
                  "finishTime": "2026-08-25T09:32:09Z",
                  "definition": { "name": "WitherbyConnect" }
                },
                {
                  "id": 453,
                  "buildNumber": "20260825.14",
                  "status": "inProgress",
                  "reason": "pullRequest",
                  "sourceBranch": "refs/pull/327/merge",
                  "triggerInfo": {
                    "pr.number": "327",
                    "pr.sourceBranch": "refs/heads/feature/foo"
                  },
                  "queueTime": "2026-08-25T10:00:00Z",
                  "startTime": "2026-08-25T10:01:00Z",
                  "definition": { "name": "WitherbyConnect" }
                }
              ]
            }
            """;

        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })));

        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "secret-pat",
            CancellationToken.None);

        Assert.Equal(AzureBuildPollOutcome.Ok, result.Outcome);
        var ci = Assert.Single(result.Runs, r => r.RunId == 452);
        Assert.Equal("20260825.13", ci.BuildNumber);
        Assert.Null(ci.PullRequestNumber);
        Assert.Equal("master", ci.Branch);
        Assert.Equal("691538acd954126677fabf666d8af886ad094a27", ci.SourceVersion);
        Assert.Contains("buildId=452", ci.RunUrl, StringComparison.Ordinal);

        var pr = Assert.Single(result.Runs, r => r.RunId == 453);
        Assert.Equal(327, pr.PullRequestNumber);
        Assert.Equal("feature/foo", pr.Branch);
        Assert.Equal("refs/heads/feature/foo", pr.SourceBranchRef);
        Assert.Equal("20260825.14", pr.BuildNumber);
    }

    [Fact]
    public async Task Pr_merge_ref_resolves_source_branch_from_parameters_when_trigger_omits_it()
    {
        const string json = """
            {
              "value": [
                {
                  "id": 498,
                  "buildNumber": "20260828.10",
                  "status": "completed",
                  "result": "succeeded",
                  "reason": "pullRequest",
                  "sourceBranch": "refs/pull/188/merge",
                  "triggerInfo": {
                    "pr.number": "188",
                    "pr.isFork": "False"
                  },
                  "parameters": "{\"system.pullRequest.sourceBranch\":\"refs/heads/feature/AB-408-dataset-xml-security\",\"system.pullRequest.pullRequestId\":\"188\"}",
                  "queueTime": "2026-08-28T08:56:37Z",
                  "definition": { "name": "WitherbyConnect" }
                }
              ]
            }
            """;

        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })));

        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "secret-pat",
            CancellationToken.None);

        var run = Assert.Single(result.Runs);
        Assert.Equal(188, run.PullRequestNumber);
        Assert.Equal("feature/AB-408-dataset-xml-security", run.Branch);
        Assert.Equal("refs/heads/feature/AB-408-dataset-xml-security", run.SourceBranchRef);
    }

    [Fact]
    public async Task Malformed_parameters_json_does_not_break_poll()
    {
        const string json = """
            {
              "value": [
                {
                  "id": 501,
                  "buildNumber": "20260828.11",
                  "status": "completed",
                  "result": "succeeded",
                  "reason": "pullRequest",
                  "sourceBranch": "refs/pull/185/merge",
                  "triggerInfo": { "pr.number": "185" },
                  "parameters": "{not-json",
                  "queueTime": "2026-08-28T09:00:00Z",
                  "definition": { "name": "CI" }
                }
              ]
            }
            """;

        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })));

        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "pat",
            CancellationToken.None);

        Assert.Equal(AzureBuildPollOutcome.Ok, result.Outcome);
        var run = Assert.Single(result.Runs);
        Assert.Equal("PR #185", run.Branch);
        Assert.Null(run.SourceBranchRef);
    }

    [Fact]
    public async Task Parameters_merge_ref_is_rejected()
    {
        const string json = """
            {
              "value": [
                {
                  "id": 502,
                  "buildNumber": "20260828.12",
                  "status": "completed",
                  "result": "succeeded",
                  "reason": "pullRequest",
                  "sourceBranch": "refs/pull/185/merge",
                  "triggerInfo": { "pr.number": "185" },
                  "parameters": "{\"system.pullRequest.sourceBranch\":\"refs/pull/185/merge\"}",
                  "queueTime": "2026-08-28T09:00:00Z",
                  "definition": { "name": "CI" }
                }
              ]
            }
            """;

        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })));

        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "pat",
            CancellationToken.None);

        var run = Assert.Single(result.Runs);
        Assert.Equal("PR #185", run.Branch);
        Assert.Null(run.SourceBranchRef);
    }

    [Fact]
    public async Task Missing_buildNumber_stays_null()
    {
        const string json = """
            {
              "value": [
                {
                  "id": 99,
                  "status": "completed",
                  "result": "succeeded",
                  "sourceBranch": "refs/heads/main",
                  "queueTime": "2026-08-25T10:00:00Z",
                  "definition": { "name": "CI" }
                }
              ]
            }
            """;

        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })));

        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "pat",
            CancellationToken.None);

        var run = Assert.Single(result.Runs);
        Assert.Equal(99, run.RunId);
        Assert.Null(run.BuildNumber);
    }

    [Fact]
    public async Task SourceVersion_parsed_from_build_api()
    {
        const string sha = "691538acd954126677fabf666d8af886ad094a27";
        const string json = $$"""
            {
              "value": [
                {
                  "id": 505,
                  "buildNumber": "20260828.17",
                  "status": "completed",
                  "result": "succeeded",
                  "sourceBranch": "refs/heads/feature/deleted",
                  "sourceVersion": "{{sha}}",
                  "queueTime": "2026-08-28T12:00:00Z",
                  "definition": { "name": "CI" }
                }
              ]
            }
            """;

        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })));

        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "pat",
            CancellationToken.None);

        var run = Assert.Single(result.Runs);
        Assert.Equal(sha, run.SourceVersion);
    }

    [Fact]
    public async Task Malformed_json_is_unavailable()
    {
        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
            })));

        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "pat",
            CancellationToken.None);

        Assert.Equal(AzureBuildPollOutcome.Unavailable, result.Outcome);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            return Task.FromResult(handler(request));
        }
    }
}
