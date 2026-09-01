using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>On-demand Git ref lookup for lazy branch navigation only (#100).</summary>
public sealed class AzureGitRefClient : IAzureGitRefClient, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public AzureGitRefClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            ownsHttpClient = true;
        }
        else
        {
            this.httpClient = httpClient;
            ownsHttpClient = false;
        }
    }

    public async Task<AzureGitRefLookupResult> BranchRefExistsAsync(
        string organizationUrl,
        string adoProjectIdOrName,
        string repositoryId,
        string branchShortName,
        string? pat,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pat))
        {
            return new AzureGitRefLookupResult(AzureGitRefOutcome.PatMissing, false, "No PAT is available.");
        }

        if (!AzureOrganizationUrl.TryNormalize(organizationUrl, out var orgUrl, out var urlError))
        {
            return new AzureGitRefLookupResult(AzureGitRefOutcome.Unavailable, false, urlError);
        }

        if (string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(branchShortName))
        {
            return new AzureGitRefLookupResult(AzureGitRefOutcome.Unavailable, false, "Invalid repository or branch.");
        }

        var projectSegment = Uri.EscapeDataString(adoProjectIdOrName.Trim());
        var repoSegment = Uri.EscapeDataString(repositoryId.Trim());
        var filter = Uri.EscapeDataString($"heads/{branchShortName.Trim()}");
        var url = AzureDevOpsRequestFactory.ApiUrl(
            orgUrl,
            $"/{projectSegment}/_apis/git/repositories/{repoSegment}/refs?filter={filter}&api-version={AzureDevOpsApiVersions.RestApi}");

        try
        {
            using var request = AzureDevOpsRequestFactory.CreateGet(url, SanitizePat(pat));
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            {
                return new AzureGitRefLookupResult(
                    AzureGitRefOutcome.AuthRequired,
                    false,
                    "Azure DevOps rejected the credentials.");
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new AzureGitRefLookupResult(
                    AzureGitRefOutcome.Unavailable,
                    false,
                    Truncate($"HTTP {(int)response.StatusCode}: {StripSecrets(body)}", 160));
            }

            return ParseExists(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException or IOException)
        {
            return new AzureGitRefLookupResult(
                AzureGitRefOutcome.Unavailable,
                false,
                Truncate(StripSecrets(ex.Message), 160));
        }
        catch (JsonException)
        {
            return new AzureGitRefLookupResult(
                AzureGitRefOutcome.Unavailable,
                false,
                "Malformed Azure DevOps refs response.");
        }
    }

    private static AzureGitRefLookupResult ParseExists(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var count = 0;
        if (doc.RootElement.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var parsedCount))
        {
            count = parsedCount;
        }
        else if (doc.RootElement.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.Array)
        {
            count = valueEl.GetArrayLength();
        }

        return new AzureGitRefLookupResult(AzureGitRefOutcome.Ok, count > 0);
    }

    private static string SanitizePat(string pat)
    {
        var trimmed = pat.Trim();
        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Bearer ".Length..].Trim();
        }

        if (trimmed.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Basic ".Length..].Trim();
        }

        return trimmed;
    }

    private static string StripSecrets(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text
            .Replace("Authorization", "Auth", StringComparison.OrdinalIgnoreCase)
            .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int max)
    {
        var t = value.Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
