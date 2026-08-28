using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Polls Azure Pipelines builds for a definition. One list request per call;
/// active runs are preferred by callers (queueTimeDescending, not finishTimeDescending).
/// Never returns mock healthy CI when PAT is missing.
/// </summary>
public sealed class AzureBuildPollClient : IAzureBuildPollClient, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public AzureBuildPollClient(HttpClient? httpClient = null)
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

    public async Task<AzureBuildPollResult> ListRecentBuildsAsync(
        string organizationUrl,
        string adoProjectIdOrName,
        int definitionId,
        string pipelineDisplayName,
        string? pat,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pat))
        {
            return new AzureBuildPollResult(
                AzureBuildPollOutcome.PatMissing,
                [],
                "No PAT is available for this connection.");
        }

        if (!AzureOrganizationUrl.TryNormalize(organizationUrl, out var orgUrl, out var urlError))
        {
            return new AzureBuildPollResult(AzureBuildPollOutcome.Unavailable, [], urlError);
        }

        var projectSegment = Uri.EscapeDataString(adoProjectIdOrName.Trim());
        var url = AzureDevOpsRequestFactory.ApiUrl(
            orgUrl,
            $"/{projectSegment}/_apis/build/builds?definitions={definitionId}&$top=25&queryOrder=queueTimeDescending&api-version={AzureDevOpsApiVersions.RestApi}");

        try
        {
            using var request = AzureDevOpsRequestFactory.CreateGet(url, SanitizePat(pat));
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            {
                return new AzureBuildPollResult(
                    AzureBuildPollOutcome.AuthRequired,
                    [],
                    "Azure DevOps rejected the credentials.");
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new AzureBuildPollResult(
                    AzureBuildPollOutcome.Unavailable,
                    [],
                    Truncate($"HTTP {(int)response.StatusCode}: {StripSecrets(body)}", 160));
            }

            return ParseRuns(body, definitionId, pipelineDisplayName, orgUrl, adoProjectIdOrName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException or IOException)
        {
            return new AzureBuildPollResult(
                AzureBuildPollOutcome.Unavailable,
                [],
                Truncate(StripSecrets(ex.Message), 160));
        }
        catch (JsonException)
        {
            return new AzureBuildPollResult(
                AzureBuildPollOutcome.Unavailable,
                [],
                "Malformed Azure DevOps response.");
        }
    }

    private static AzureBuildPollResult ParseRuns(
        string json,
        int definitionId,
        string pipelineDisplayName,
        string orgUrl,
        string adoProject)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return new AzureBuildPollResult(
                AzureBuildPollOutcome.Unavailable,
                [],
                "Malformed Azure DevOps response.");
        }

        var runs = new List<AzurePipelineRunInfo>();
        foreach (var run in value.EnumerateArray())
        {
            var runId = run.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var idVal) ? idVal : 0L;
            if (runId <= 0)
            {
                continue;
            }

            var buildNumber = run.TryGetProperty("buildNumber", out var bn) && bn.ValueKind == JsonValueKind.String
                ? bn.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(buildNumber))
            {
                buildNumber = null;
            }

            var state = ParseState(run.TryGetProperty("status", out var st) ? st.GetString() : null);
            var result = ParseResult(run.TryGetProperty("result", out var rs) && rs.ValueKind != JsonValueKind.Null
                ? rs.GetString()
                : null);
            var branchRaw = run.TryGetProperty("sourceBranch", out var br) ? br.GetString() : null;
            var reason = run.TryGetProperty("reason", out var reasonEl) ? reasonEl.GetString() : null;
            JsonElement? triggerInfo = run.TryGetProperty("triggerInfo", out var ti) && ti.ValueKind == JsonValueKind.Object
                ? ti
                : null;
            var buildParameters = run.TryGetProperty("parameters", out var paramsEl)
                                  && paramsEl.ValueKind == JsonValueKind.String
                ? paramsEl.GetString()
                : null;
            var pullRequestNumber = AzurePullRequestMetadata.TryResolveNumber(reason, branchRaw, triggerInfo);
            var branch = AzurePullRequestMetadata.ResolveDisplayBranch(
                branchRaw,
                pullRequestNumber,
                triggerInfo,
                buildParameters);
            var sourceBranchRef = AzurePullRequestMetadata.ResolveSourceBranchRef(
                branchRaw,
                triggerInfo,
                buildParameters);
            var queuedAt = ParseDate(run, "queueTime") ?? DateTimeOffset.UtcNow;
            var startedAt = ParseDate(run, "startTime");
            var finishedAt = ParseDate(run, "finishTime");
            var name = pipelineDisplayName;
            if (run.TryGetProperty("definition", out var def) && def.TryGetProperty("name", out var defName))
            {
                var n = defName.GetString();
                if (!string.IsNullOrWhiteSpace(n))
                {
                    name = n;
                }
            }

            var runUrl = AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(orgUrl, adoProject, runId);
            runs.Add(new AzurePipelineRunInfo(
                definitionId,
                name,
                runId,
                buildNumber,
                state,
                result,
                branch,
                queuedAt,
                startedAt,
                finishedAt,
                runUrl,
                pullRequestNumber,
                sourceBranchRef));
        }

        return new AzureBuildPollResult(AzureBuildPollOutcome.Ok, runs);
    }

    private static DateTimeOffset? ParseDate(JsonElement run, string name)
    {
        if (!run.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var s = el.GetString();
        return DateTimeOffset.TryParse(s, out var parsed) ? parsed : null;
    }

    private static PipelineRunState ParseState(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "inprogress" => PipelineRunState.InProgress,
            "completed" => PipelineRunState.Completed,
            "notstarted" => PipelineRunState.NotStarted,
            "cancelling" => PipelineRunState.Canceling,
            _ => PipelineRunState.Unknown
        };

    private static PipelineRunResult ParseResult(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "succeeded" => PipelineRunResult.Succeeded,
            "partiallysucceeded" => PipelineRunResult.PartiallySucceeded,
            "failed" => PipelineRunResult.Failed,
            "canceled" => PipelineRunResult.Canceled,
            _ => PipelineRunResult.Unknown
        };

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
        // Avoid echoing Authorization headers or token-like blobs into UI.
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
