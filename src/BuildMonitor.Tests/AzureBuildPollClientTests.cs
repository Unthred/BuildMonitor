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
    public async Task Ok_parses_runs_without_finish_time_order_assumption()
    {
        const string json = """
            {
              "value": [
                {
                  "id": 50,
                  "buildNumber": "50",
                  "status": "inProgress",
                  "sourceBranch": "refs/heads/master",
                  "queueTime": "2026-08-25T10:00:00Z",
                  "startTime": "2026-08-25T10:01:00Z",
                  "definition": { "name": "WitherbyConnect" }
                },
                {
                  "id": 49,
                  "buildNumber": "49",
                  "status": "completed",
                  "result": "failed",
                  "sourceBranch": "refs/heads/master",
                  "queueTime": "2026-08-25T09:00:00Z",
                  "finishTime": "2026-08-25T09:30:00Z",
                  "definition": { "name": "WitherbyConnect" }
                }
              ]
            }
            """;

        using var client = new AzureBuildPollClient(new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return response;
        })));

        var result = await client.ListRecentBuildsAsync(
            "https://dev.azure.com/org",
            "proj",
            8,
            "CI",
            "secret-pat",
            CancellationToken.None);

        Assert.Equal(AzureBuildPollOutcome.Ok, result.Outcome);
        Assert.Equal(2, result.Runs.Count);
        Assert.Contains(result.Runs, r => r.RunId == 50 && r.State == Core.Models.PipelineRunState.InProgress);
        Assert.DoesNotContain("secret-pat", result.Runs[0].RunUrl, StringComparison.OrdinalIgnoreCase);
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
