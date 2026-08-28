using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>On-demand build timeline fetch for lazy failure navigation only.</summary>
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

            return ParseTimeline(body);
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

    private static AzureBuildTimelineResult ParseTimeline(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("records", out var recordsEl)
            || recordsEl.ValueKind != JsonValueKind.Array)
        {
            return new AzureBuildTimelineResult(
                AzureBuildTimelineOutcome.Unavailable,
                [],
                "Malformed Azure DevOps timeline response.");
        }

        var records = new List<AzureBuildTimelineRecord>();
        foreach (var record in recordsEl.EnumerateArray())
        {
            if (!record.TryGetProperty("id", out var idEl)
                || !Guid.TryParse(idEl.GetString(), out var id))
            {
                continue;
            }

            Guid? parentId = null;
            if (record.TryGetProperty("parentId", out var parentEl)
                && parentEl.ValueKind == JsonValueKind.String
                && Guid.TryParse(parentEl.GetString(), out var parsedParent))
            {
                parentId = parsedParent;
            }

            var type = record.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString() ?? string.Empty
                : string.Empty;
            var result = record.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.String
                ? resultEl.GetString()
                : null;
            var name = record.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString()
                : null;

            records.Add(new AzureBuildTimelineRecord(id, parentId, type, result, name));
        }

        return new AzureBuildTimelineResult(AzureBuildTimelineOutcome.Ok, records);
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
