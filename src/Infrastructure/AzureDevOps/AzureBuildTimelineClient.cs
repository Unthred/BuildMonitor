using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Builds timeline HTTP client shared by failure-log navigation and active-run execution attach.
/// </summary>
public sealed class AzureBuildTimelineClient : IAzureBuildTimelineClient, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public AzureBuildTimelineClient(HttpClient? httpClient = null)
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

    public async Task<AzureBuildTimelineResult> GetTimelineAsync(
        string organizationUrl,
        string adoProjectIdOrName,
        long buildId,
        string? pat,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pat))
        {
            return new AzureBuildTimelineResult(
                AzureBuildTimelineOutcome.PatMissing,
                [],
                "No PAT is available for this connection.");
        }

        if (!AzureOrganizationUrl.TryNormalize(organizationUrl, out var orgUrl, out var urlError))
        {
            return new AzureBuildTimelineResult(AzureBuildTimelineOutcome.Unavailable, [], urlError);
        }

        if (buildId <= 0)
        {
            return new AzureBuildTimelineResult(
                AzureBuildTimelineOutcome.Unavailable,
                [],
                "Invalid build id.");
        }

        var projectSegment = Uri.EscapeDataString(adoProjectIdOrName.Trim());
        var url = AzureDevOpsRequestFactory.ApiUrl(
            orgUrl,
            $"/{projectSegment}/_apis/build/builds/{buildId}/timeline?api-version={AzureDevOpsApiVersions.RestApi}");

        try
        {
            using var request = AzureDevOpsRequestFactory.CreateGet(url, SanitizePat(pat));
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            {
                return new AzureBuildTimelineResult(
                    AzureBuildTimelineOutcome.AuthRequired,
                    [],
                    "Azure DevOps rejected the credentials.");
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new AzureBuildTimelineResult(
                    AzureBuildTimelineOutcome.Unavailable,
                    [],
                    Truncate($"HTTP {(int)response.StatusCode}: {StripSecrets(body)}", 160));
            }

            return AzureBuildTimelineParser.Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException or IOException)
        {
            return new AzureBuildTimelineResult(
                AzureBuildTimelineOutcome.Unavailable,
                [],
                Truncate(StripSecrets(ex.Message), 160));
        }
        catch (JsonException)
        {
            return new AzureBuildTimelineResult(
                AzureBuildTimelineOutcome.Unavailable,
                [],
                "Malformed Azure DevOps timeline response.");
        }
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
