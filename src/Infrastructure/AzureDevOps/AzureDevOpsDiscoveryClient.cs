using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps connection test and discovery using REST API <see cref="AzureDevOpsApiVersions.RestApi"/>.
/// Does not set credentials on shared <see cref="HttpClient.DefaultRequestHeaders"/>.
/// Never fabricates success when a PAT is missing.
/// </summary>
public sealed class AzureDevOpsDiscoveryClient : IAzureDevOpsDiscoveryClient, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public AzureDevOpsDiscoveryClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            ownsHttpClient = true;
        }
        else
        {
            this.httpClient = httpClient;
            ownsHttpClient = false;
        }
    }

    public async Task<AzureConnectionTestResult> TestConnectionAsync(
        AzureDevOpsConnectionSettings connection,
        string? pat,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pat))
        {
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.PatMissing,
                "No PAT is available for this connection. Enter a personal access token and save it.");
        }

        if (!AzureOrganizationUrl.TryNormalize(connection.OrganizationUrl, out var orgUrl, out var urlError))
        {
            return new AzureConnectionTestResult(AzureConnectionTestOutcome.OrganizationUnreachable, urlError);
        }

        try
        {
            var url = AzureDevOpsRequestFactory.ApiUrl(
                orgUrl,
                $"/_apis/connectionData?api-version={AzureDevOpsApiVersions.RestApi}");
            using var request = AzureDevOpsRequestFactory.CreateGet(url, pat);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new AzureConnectionTestResult(
                    AzureConnectionTestOutcome.AuthenticationRejected,
                    "Azure DevOps rejected the credentials (401/403). Check the PAT and organisation URL.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapHttpFailure(response.StatusCode, body);
            }

            string? displayName = null;
            try
            {
                displayName = AzureDevOpsDiscoveryJson.ParseAuthenticatedUserDisplayName(body);
            }
            catch (JsonException)
            {
                return new AzureConnectionTestResult(
                    AzureConnectionTestOutcome.UnexpectedResponse,
                    "Connected but the Azure connectionData response could not be parsed.");
            }

            var message = string.IsNullOrWhiteSpace(displayName)
                ? "Connection succeeded."
                : $"Connection succeeded as {displayName}.";
            return new AzureConnectionTestResult(AzureConnectionTestOutcome.Success, message, displayName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.NetworkFailure,
                $"Network error contacting Azure DevOps: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.OrganizationUnreachable,
                $"Organisation URL unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.NetworkFailure,
                "The Azure DevOps request timed out.");
        }
    }

    public async Task<IReadOnlyList<AzureProjectSummary>> ListProjectsAsync(
        AzureDevOpsConnectionSettings connection,
        string pat,
        CancellationToken cancellationToken)
    {
        EnsurePat(pat);
        var orgUrl = RequireOrgUrl(connection);
        var url = AzureDevOpsRequestFactory.ApiUrl(
            orgUrl,
            $"/_apis/projects?stateFilter=WellFormed&api-version={AzureDevOpsApiVersions.RestApi}");
        var json = await SendForJsonAsync(url, pat, cancellationToken);
        return AzureDevOpsDiscoveryJson.ParseProjects(json);
    }

    public async Task<IReadOnlyList<AzureRepositorySummary>> ListRepositoriesAsync(
        AzureDevOpsConnectionSettings connection,
        string pat,
        string projectIdOrName,
        CancellationToken cancellationToken)
    {
        EnsurePat(pat);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectIdOrName);
        var orgUrl = RequireOrgUrl(connection);
        var encodedProject = Uri.EscapeDataString(projectIdOrName.Trim());
        var url = AzureDevOpsRequestFactory.ApiUrl(
            orgUrl,
            $"/{encodedProject}/_apis/git/repositories?includeAllUrls=true&api-version={AzureDevOpsApiVersions.RestApi}");
        var json = await SendForJsonAsync(url, pat, cancellationToken);
        return AzureDevOpsDiscoveryJson.ParseRepositories(json);
    }

    public async Task<IReadOnlyList<AzurePipelineSummary>> ListPipelinesForRepositoryAsync(
        AzureDevOpsConnectionSettings connection,
        string pat,
        string projectIdOrName,
        string repositoryId,
        CancellationToken cancellationToken)
    {
        EnsurePat(pat);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectIdOrName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        var orgUrl = RequireOrgUrl(connection);
        var encodedProject = Uri.EscapeDataString(projectIdOrName.Trim());
        var encodedRepo = Uri.EscapeDataString(repositoryId.Trim());

        // Official Build Definitions - List supports repositoryId + repositoryType=TfsGit.
        // includeAllProperties=true is needed for triggers / repository details.
        var url = AzureDevOpsRequestFactory.ApiUrl(
            orgUrl,
            $"/{encodedProject}/_apis/build/definitions?repositoryId={encodedRepo}&repositoryType=TfsGit&includeAllProperties=true&api-version={AzureDevOpsApiVersions.RestApi}");
        var json = await SendForJsonAsync(url, pat, cancellationToken);
        return AzureDevOpsDiscoveryJson.ParsePipelines(json, orgUrl, projectIdOrName.Trim(), repositoryId.Trim());
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private async Task<string> SendForJsonAsync(string url, string pat, CancellationToken cancellationToken)
    {
        using var request = AzureDevOpsRequestFactory.CreateGet(url, pat);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AzureDevOpsDiscoveryException(
                AzureConnectionTestOutcome.AuthenticationRejected,
                "Azure DevOps rejected the credentials (401/403).");
        }

        if (!response.IsSuccessStatusCode)
        {
            var mapped = MapHttpFailure(response.StatusCode, body);
            throw new AzureDevOpsDiscoveryException(mapped.Outcome, mapped.Message);
        }

        return body;
    }

    private static void EnsurePat(string pat)
    {
        if (string.IsNullOrWhiteSpace(pat))
        {
            throw new AzureDevOpsDiscoveryException(
                AzureConnectionTestOutcome.PatMissing,
                "No PAT is available for this connection.");
        }
    }

    private static string RequireOrgUrl(AzureDevOpsConnectionSettings connection)
    {
        if (!AzureOrganizationUrl.TryNormalize(connection.OrganizationUrl, out var orgUrl, out var error))
        {
            throw new AzureDevOpsDiscoveryException(AzureConnectionTestOutcome.OrganizationUnreachable, error);
        }

        return orgUrl;
    }

    private static AzureConnectionTestResult MapHttpFailure(HttpStatusCode statusCode, string body)
    {
        if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.OrganizationUnreachable,
                $"Azure DevOps returned {(int)statusCode}. Check the organisation URL.");
        }

        var snippet = body.Length > 180 ? body[..180] + "…" : body;
        return new AzureConnectionTestResult(
            AzureConnectionTestOutcome.UnexpectedResponse,
            $"Unexpected Azure DevOps response {(int)statusCode}: {snippet}");
    }

    private static bool IsTransportFailure(Exception ex) =>
        ex is SocketException
        || ex is IOException
        || (ex is HttpRequestException hre && hre.InnerException is SocketException);
}

/// <summary>Structured discovery failure suitable for UI (not a raw HTTP exception).</summary>
public sealed class AzureDevOpsDiscoveryException(
    AzureConnectionTestOutcome outcome,
    string message) : Exception(message)
{
    public AzureConnectionTestOutcome Outcome { get; } = outcome;
}
