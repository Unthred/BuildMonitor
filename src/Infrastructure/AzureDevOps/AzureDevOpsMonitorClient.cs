using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.AzureDevOps;

public sealed class AzureDevOpsMonitorClient(HttpClient httpClient, Func<Task<string?>> getPatAsync) : IAzureDevOpsMonitorClient
{
    public async Task<MonitorSnapshot> GetSnapshotAsync(
        AzureDevOpsSettings settings,
        CancellationToken cancellationToken)
    {
        var pat = await getPatAsync();
        if (string.IsNullOrWhiteSpace(pat))
        {
            return BuildMockSnapshot(settings);
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

        var list = new List<PipelineSnapshot>(settings.Pipelines.Count);
        foreach (var pipeline in settings.Pipelines)
        {
            list.Add(await GetLatestPipelineAsync(settings, pipeline, cancellationToken));
        }

        return new MonitorSnapshot(DateTimeOffset.UtcNow, list);
    }

    private async Task<PipelineSnapshot> GetLatestPipelineAsync(
        AzureDevOpsSettings settings,
        MonitoredPipelineSettings pipeline,
        CancellationToken cancellationToken)
    {
        var branchFilter = pipeline.IncludedBranches.FirstOrDefault();
        var url = $"{settings.OrganizationUrl.TrimEnd('/')}/{settings.Project}/_apis/build/builds?definitions={pipeline.PipelineId}&$top=1&queryOrder=finishTimeDescending&api-version=7.1";
        if (!string.IsNullOrWhiteSpace(branchFilter))
        {
            url += $"&branchName={Uri.EscapeDataString(branchFilter)}";
        }

        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
        {
            return new PipelineSnapshot(
                pipeline.PipelineId,
                string.IsNullOrWhiteSpace(pipeline.DisplayName) ? $"Pipeline {pipeline.PipelineId}" : pipeline.DisplayName,
                0,
                "No runs",
                PipelineRunState.NotStarted,
                PipelineRunResult.Unknown,
                branchFilter ?? "refs/heads/main",
                null,
                null,
                DateTimeOffset.UtcNow,
                null,
                null,
                $"{settings.OrganizationUrl.TrimEnd('/')}/{settings.Project}/_build",
                []);
        }

        var run = value[0];
        var runId = run.GetProperty("id").GetInt64();
        var runName = run.GetProperty("buildNumber").GetString() ?? runId.ToString();
        var runState = ParseState(run.GetProperty("status").GetString());
        var runResult = ParseResult(run.TryGetProperty("result", out var result) ? result.GetString() : null);
        var branch = run.GetProperty("sourceBranch").GetString() ?? "refs/heads/main";
        var requestedBy = run.TryGetProperty("requestedFor", out var req) && req.TryGetProperty("displayName", out var reqName)
            ? reqName.GetString()
            : null;
        var commit = run.TryGetProperty("sourceVersion", out var source) ? source.GetString() : null;
        var queuedAt = ParseDate(run.TryGetProperty("queueTime", out var queueTime) ? queueTime.GetString() : null) ?? DateTimeOffset.UtcNow;
        var startedAt = ParseDate(run.TryGetProperty("startTime", out var startTime) ? startTime.GetString() : null);
        var finishedAt = ParseDate(run.TryGetProperty("finishTime", out var finishTime) ? finishTime.GetString() : null);

        var pipelineName = run.TryGetProperty("definition", out var definition) && definition.TryGetProperty("name", out var defName)
            ? defName.GetString() ?? pipeline.DisplayName
            : pipeline.DisplayName;

        return new PipelineSnapshot(
            pipeline.PipelineId,
            string.IsNullOrWhiteSpace(pipelineName) ? $"Pipeline {pipeline.PipelineId}" : pipelineName,
            runId,
            runName,
            runState,
            runResult,
            branch,
            commit,
            requestedBy,
            queuedAt,
            startedAt,
            finishedAt,
            $"{settings.OrganizationUrl.TrimEnd('/')}/{settings.Project}/_build/results?buildId={runId}&view=results",
            BuildSyntheticStages(runState, runResult));
    }

    private static IReadOnlyList<StageSnapshot> BuildSyntheticStages(PipelineRunState state, PipelineRunResult result)
    {
        // Azure Build API does not always expose stage detail directly in one call.
        // Keep one synthetic summary stage for MVP until timeline expansion is added.
        return
        [
            new StageSnapshot(
                "PipelineSummary",
                state,
                result,
                null,
                null,
                null)
        ];
    }

    private static MonitorSnapshot BuildMockSnapshot(AzureDevOpsSettings settings)
    {
        var now = DateTimeOffset.UtcNow;
        var pipelines = settings.Pipelines
            .Select((p, index) => new PipelineSnapshot(
                p.PipelineId,
                string.IsNullOrWhiteSpace(p.DisplayName) ? $"Pipeline {p.PipelineId}" : p.DisplayName,
                now.ToUnixTimeSeconds() - index,
                $"M{now:HHmm}-{index}",
                index == 0 ? PipelineRunState.InProgress : PipelineRunState.Completed,
                index == 2 ? PipelineRunResult.Failed : PipelineRunResult.Succeeded,
                "refs/heads/main",
                null,
                "Mock user",
                now.AddMinutes(-10),
                now.AddMinutes(-8),
                now.AddMinutes(-1),
                $"{settings.OrganizationUrl.TrimEnd('/')}/{settings.Project}/_build",
                []))
            .ToList();

        return new MonitorSnapshot(now, pipelines);
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

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
