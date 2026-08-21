using System.Net;
using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;

namespace BuildMonitor.Tests;

public sealed class AzureDevOpsDiscoveryClientTests
{
    private static AzureDevOpsConnectionSettings Conn() => new()
    {
        Id = "c1",
        DisplayName = "Contoso",
        OrganizationUrl = "https://dev.azure.com/contoso"
    };

    [Fact]
    public async Task TestConnection_reports_pat_missing_without_calling_http()
    {
        var handler = new QueueHttpHandler();
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), pat: null, CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.PatMissing, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TestConnection_success_parses_projects_payload()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"count":1,"value":[{"id":"p1","name":"Proj","state":"wellFormed"}]}""");
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), "secret-pat", CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.Success, result.Outcome);
        Assert.Contains("/_apis/projects", handler.Requests[0].RequestUri!.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-pat", handler.Requests[0].Headers.Authorization!.Parameter!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnection_maps_401_to_authentication_rejected()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), "bad", CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.AuthenticationRejected, result.Outcome);
    }

    [Fact]
    public async Task TestConnection_203_anonymous_is_authentication_rejected()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.NonAuthoritativeInformation, """{"authenticatedUser":{"providerDisplayName":"Anonymous"}}""");
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), "pat", CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.AuthenticationRejected, result.Outcome);
        Assert.Contains("203", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnection_strips_bearer_prefix_from_pasted_pat()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"count":0,"value":[]}""");
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        _ = await client.TestConnectionAsync(Conn(), "Bearer my-real-pat-token", CancellationToken.None);
        var parameter = handler.Requests[0].Headers.Authorization!.Parameter!;
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(parameter));
        Assert.Equal(":my-real-pat-token", decoded);
    }

    [Fact]
    public async Task TestConnection_maps_network_failure()
    {
        var handler = new QueueHttpHandler { ThrowOnSend = new HttpRequestException("no route") };
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), "pat", CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.NetworkFailure, result.Outcome);
        Assert.Contains("Network error", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnection_timeout_maps_to_network_failure()
    {
        var handler = new QueueHttpHandler { ThrowOnSend = new TaskCanceledException("timeout") };
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), "pat", CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.NetworkFailure, result.Outcome);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnection_caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new QueueHttpHandler
        {
            ThrowOnSend = new OperationCanceledException(cts.Token)
        };
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.TestConnectionAsync(Conn(), "pat", cts.Token));
    }

    [Fact]
    public async Task TestConnection_404_maps_to_organization_unreachable()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), "pat", CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.OrganizationUnreachable, result.Outcome);
    }

    [Fact]
    public async Task TestConnection_malformed_success_payload_is_unexpected_response()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{ not-json");
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), "pat", CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.UnexpectedResponse, result.Outcome);
    }

    [Fact]
    public async Task TestConnection_400_includes_detail_and_is_organization_unreachable()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"message":"The requested REST API version is out of range."}""");
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var result = await client.TestConnectionAsync(Conn(), "pat", CancellationToken.None);
        Assert.Equal(AzureConnectionTestOutcome.OrganizationUnreachable, result.Outcome);
        Assert.Contains("400", result.Message, StringComparison.Ordinal);
        Assert.Contains("out of range", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListProjects_parses_value_array()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            {"count":1,"value":[{"id":"p1","name":"Proj","description":"d","state":"wellFormed"}]}
            """);
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var projects = await client.ListProjectsAsync(Conn(), "pat", CancellationToken.None);
        Assert.Single(projects);
        Assert.Equal("p1", projects[0].Id);
        Assert.Equal("Proj", projects[0].Name);
    }

    [Fact]
    public async Task ListRepositories_normalizes_default_branch()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            {"value":[{"id":"r1","name":"Repo","defaultBranch":"refs/heads/main","remoteUrl":"https://dev.azure.com/contoso/P/_git/Repo","webUrl":"https://dev.azure.com/contoso/P/_git/Repo","project":{"id":"p1","name":"P"}}]}
            """);
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var repos = await client.ListRepositoriesAsync(Conn(), "pat", "P", CancellationToken.None);
        Assert.Single(repos);
        Assert.Equal("main", repos[0].DefaultBranchShortName);
        Assert.Equal("r1", repos[0].Id);
    }

    [Fact]
    public async Task ListPipelines_supports_multiple_and_zero()
    {
        var handler = new QueueHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            {"value":[
              {"id":10,"name":"CI","queueStatus":"enabled","path":"\\","repository":{"id":"r1","name":"Repo","type":"TfsGit"},"triggers":[{"branchFilters":["+refs/heads/main"]}]},
              {"id":11,"name":"PR","queueStatus":"disabled","path":"\\","repository":{"id":"r1","name":"Repo","type":"TfsGit"},"triggers":[]}
            ]}
            """);
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(handler));
        var pipelines = await client.ListPipelinesForRepositoryAsync(Conn(), "pat", "P", "r1", CancellationToken.None);
        Assert.Equal(2, pipelines.Count);
        Assert.Contains(pipelines, p => p.DefinitionId == 10 && p.IsEnabled && p.TriggerBranches.Contains("main"));
        Assert.Contains(pipelines, p => p.DefinitionId == 11 && !p.IsEnabled);

        handler.Enqueue(HttpStatusCode.OK, """{"value":[]}""");
        var empty = await client.ListPipelinesForRepositoryAsync(Conn(), "pat", "P", "r1", CancellationToken.None);
        Assert.Empty(empty);
    }

    [Fact]
    public async Task ListProjects_missing_pat_throws_structured()
    {
        using var client = new AzureDevOpsDiscoveryClient(new HttpClient(new QueueHttpHandler()));
        var ex = await Assert.ThrowsAsync<AzureDevOpsDiscoveryException>(() =>
            client.ListProjectsAsync(Conn(), "  ", CancellationToken.None));
        Assert.Equal(AzureConnectionTestOutcome.PatMissing, ex.Outcome);
    }

    private sealed class QueueHttpHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> responses = new();
        public List<HttpRequestMessage> Requests { get; } = [];
        public Exception? ThrowOnSend { get; set; }

        public void Enqueue(HttpStatusCode status, string body) => responses.Enqueue((status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            var (status, body) = responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
