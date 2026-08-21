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
            // Prefer Projects ($top=1) over connectionData: anonymous/unauthenticated
            // calls often return HTTP 203, which must not be treated as success.
            var url = AzureDevOpsRequestFactory.ApiUrl(
                orgUrl,
                $"/_apis/projects?$top=1&api-version={AzureDevOpsApiVersions.RestApi}");
            using var request = AzureDevOpsRequestFactory.CreateGet(url, SanitizePat(pat));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new AzureConnectionTestResult(
                    AzureConnectionTestOutcome.AuthenticationRejected,
                    "Azure DevOps rejected the credentials (401/403). Check the PAT and organisation URL.");
            }

            // 203 Non-Authoritative Information is commonly returned for anonymous access.
            if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            {
                return new AzureConnectionTestResult(
                    AzureConnectionTestOutcome.AuthenticationRejected,
                    "Azure DevOps did not accept the PAT (anonymous/203 response). Check the token value and scopes.");
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return MapHttpFailure(response.StatusCode, body);
            }

            try
            {
                _ = AzureDevOpsDiscoveryJson.ParseProjects(body);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return new AzureConnectionTestResult(
                    AzureConnectionTestOutcome.UnexpectedResponse,
                    "Connected but the Azure projects response could not be parsed.");
            }

            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.Success,
                "Connection succeeded — organisation is reachable with this PAT.");
        }
        // Caller cancellation must win over HttpClient timeout (also OperationCanceledException).
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient.Timeout (and similar) surfaces as TaskCanceledException without caller cancel.
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.NetworkFailure,
                "The Azure DevOps request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.NetworkFailure,
                $"Network error contacting Azure DevOps: {ex.Message}");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.NetworkFailure,
                $"Network error contacting Azure DevOps: {ex.Message}");
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
        using var request = AzureDevOpsRequestFactory.CreateGet(url, SanitizePat(pat));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AzureDevOpsDiscoveryException(
                AzureConnectionTestOutcome.AuthenticationRejected,
                "Azure DevOps rejected the credentials (401/403).");
        }

        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
        {
            throw new AzureDevOpsDiscoveryException(
                AzureConnectionTestOutcome.AuthenticationRejected,
                "Azure DevOps did not accept the PAT (anonymous/203 response).");
        }

        if (response.StatusCode != HttpStatusCode.OK)
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
        var snippet = string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : (body.Length > 240 ? body[..240] + "…" : body).Replace('\r', ' ').Replace('\n', ' ');

        if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            var detail = string.IsNullOrWhiteSpace(snippet) ? string.Empty : $" Details: {snippet}";
            return new AzureConnectionTestResult(
                AzureConnectionTestOutcome.OrganizationUnreachable,
                $"Azure DevOps returned {(int)statusCode}. Check the organisation URL (https://dev.azure.com/{{org}}).{detail}");
        }

        return new AzureConnectionTestResult(
            AzureConnectionTestOutcome.UnexpectedResponse,
            string.IsNullOrWhiteSpace(snippet)
                ? $"Unexpected Azure DevOps response {(int)statusCode}."
                : $"Unexpected Azure DevOps response {(int)statusCode}: {snippet}");
    }

    /// <summary>Trims PAT and strips accidental auth scheme prefixes from paste.</summary>
    private static string SanitizePat(string pat)
    {
        var value = pat.Trim();
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["Bearer ".Length..].Trim();
        }
        else if (value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["Basic ".Length..].Trim();
        }

        return value;
    }
}

/// <summary>Structured discovery failure suitable for UI (not a raw HTTP exception).</summary>
public sealed class AzureDevOpsDiscoveryException(
    AzureConnectionTestOutcome outcome,
    string message) : Exception(message)
{
    public AzureConnectionTestOutcome Outcome { get; } = outcome;
}
