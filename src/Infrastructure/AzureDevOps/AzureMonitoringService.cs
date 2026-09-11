using System.Collections.Concurrent;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.Git;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Adaptive Azure build polling for active projects with selected pipelines.
/// Publishes in-memory facets only (no notifications, no parallel tray authority).
/// </summary>
public sealed class AzureMonitoringService : IDisposable
{
    // Settled: pick up newly queued runs without multi-minute lag.
    // Active: while Azure reports in-progress / not-started / canceling.
    // Failure backoff: auth/network only — keep capped so a flaky PAT cannot stall CI visibility.
    public static readonly TimeSpan SettledInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan FailureBackoffInitial = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan FailureBackoffMax = TimeSpan.FromSeconds(45);

    private readonly IAzureBuildPollClient pollClient;
    private readonly IAzureBuildTimelineClient timelineClient;
    private readonly IAzureConnectionSecretStore secretStore;
    private readonly CachedLocalGitContextReader gitReader;
    private readonly Action onFacetUpdated;
    private readonly ConcurrentDictionary<string, ProjectAzureHealthFacet> facets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TimelineCacheEntry> timelineCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Last primary RunId for which a timeline attach was started (stale-response discard).</summary>
    private readonly ConcurrentDictionary<string, long> timelineRequestRunId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object settingsSync = new();
    private AppSettings settings = new();
    private CancellationTokenSource? loopCts;
    private Task? loopTask;
    private int failureStreak;
    private bool disposed;
    private readonly bool ownsTimelineClient;

    private sealed record TimelineCacheEntry(long RunId, int? ChangeId, AzureRunExecutionDetail? Detail);

    public AzureMonitoringService(
        IAzureBuildPollClient pollClient,
        IAzureConnectionSecretStore secretStore,
        ILocalGitContextReader gitReader,
        Action onFacetUpdated,
        IAzureBuildTimelineClient? timelineClient = null)
    {
        this.pollClient = pollClient;
        ownsTimelineClient = timelineClient is null;
        this.timelineClient = timelineClient ?? new AzureBuildTimelineClient();
        this.secretStore = secretStore;
        this.gitReader = gitReader as CachedLocalGitContextReader
            ?? new CachedLocalGitContextReader(gitReader);
        this.onFacetUpdated = onFacetUpdated;
        WorkerHealthRegistry.Shared.Register(
            "azure.polling",
            "Azure DevOps polling",
            TimeSpan.FromSeconds(60),
            "Background");
    }

    public ProjectAzureHealthFacet? TryGetFacet(string projectId) =>
        facets.TryGetValue(projectId, out var facet) ? facet : null;

    public void ApplySettings(AppSettings newSettings)
    {
        lock (settingsSync)
        {
            settings = newSettings;
        }

        gitReader.Invalidate();

        // Drop facets for projects that no longer qualify.
        var eligibleIds = GetEligibleProjects(newSettings).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in facets.Keys.ToList())
        {
            if (!eligibleIds.Contains(key))
            {
                facets.TryRemove(key, out _);
            }
        }

        // Zero-pipeline attached projects: publish NotMonitored without HTTP.
        foreach (var project in newSettings.Projects.Where(p =>
                     p.IsActiveInSession && p.Azure is not null && p.Azure.Pipelines.Count == 0))
        {
            facets[project.Id] = AzureFacetComposer.NotMonitored(DateTimeOffset.UtcNow);
        }

        foreach (var project in GetEligibleProjects(newSettings))
        {
            facets.TryAdd(
                project.Id,
                new ProjectAzureHealthFacet(
                    AzureMonitoringAvailability.Available,
                    AzureCiMonitoringState.NotMonitored,
                    null,
                    null,
                    [],
                    DateTimeOffset.MinValue,
                    HasSelectedPipelines: true));
        }

        onFacetUpdated();
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (loopTask is not null)
        {
            return;
        }

        loopCts = new CancellationTokenSource();
        loopTask = Task.Run(() => RunLoopAsync(loopCts.Token));
    }

    public async Task StopAsync()
    {
        var cts = loopCts;
        var task = loopTask;
        loopCts = null;
        loopTask = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        if (task is not null)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch
            {
                // ignore shutdown races
            }
        }

        cts.Dispose();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // ignore
        }

        WorkerHealthRegistry.Shared.Unregister("azure.polling");
        if (pollClient is IDisposable d)
        {
            d.Dispose();
        }

        if (ownsTimelineClient && timelineClient is IDisposable timelineDisposable)
        {
            timelineDisposable.Dispose();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                failureStreak++;
                delay = FailureBackoff(failureStreak);
            }

            WorkerHealthRegistry.Shared.Heartbeat(
                "azure.polling",
                note: $"next {delay.TotalSeconds:0}s",
                managedThreadId: Environment.CurrentManagedThreadId);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<TimeSpan> PollOnceAsync(CancellationToken cancellationToken)
    {
        AppSettings snapshot;
        lock (settingsSync)
        {
            snapshot = settings;
        }

        var eligible = GetEligibleProjects(snapshot);
        if (eligible.Count == 0)
        {
            failureStreak = 0;
            WorkerHealthRegistry.Shared.SetCurrentAction("azure.polling", "Idle (nothing to poll)");
            return SettledInterval;
        }

        WorkerHealthRegistry.Shared.SetCurrentAction("azure.polling", $"Polling {eligible.Count} project(s)");
        var anyActive = false;
        var anyFailure = false;

        foreach (var project in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var facet = await PollProjectAsync(snapshot, project, cancellationToken).ConfigureAwait(false);
            facets[project.Id] = facet;
            if (facet.Availability is AzureMonitoringAvailability.AuthRequired
                or AzureMonitoringAvailability.Unavailable)
            {
                anyFailure = true;
            }

            if (facet.CiState == AzureCiMonitoringState.Activity
                || (facet.PrimaryRun is not null && AzureRunSelector.IsActive(facet.PrimaryRun.State)))
            {
                anyActive = true;
            }
        }

        onFacetUpdated();

        if (anyFailure)
        {
            failureStreak++;
            return FailureBackoff(failureStreak);
        }

        failureStreak = 0;
        return anyActive ? ActiveInterval : SettledInterval;
    }

    private async Task<ProjectAzureHealthFacet> PollProjectAsync(
        AppSettings snapshot,
        MonitoredProjectSettings project,
        CancellationToken cancellationToken)
    {
        var azure = project.Azure!;
        var connection = snapshot.Connections.FirstOrDefault(c =>
            string.Equals(c.Id, azure.ConnectionId, StringComparison.OrdinalIgnoreCase));
        if (connection is null)
        {
            return AzureFacetComposer.Unavailable(
                DateTimeOffset.UtcNow,
                null,
                "Azure connection is missing from settings.");
        }

        string? focusBranch = null;
        if (project.Local is not null && !string.IsNullOrWhiteSpace(project.Local.RootFolder))
        {
            var git = await gitReader.ReadAsync(project.Local.RootFolder, cancellationToken).ConfigureAwait(false);
            if (git.HeadStatus == LocalGitHeadStatus.Branch)
            {
                focusBranch = git.CurrentBranch;
            }
        }

        string? pat;
        try
        {
            pat = await secretStore.LoadAsync(connection.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AzureFacetComposer.Unavailable(DateTimeOffset.UtcNow, focusBranch, "Could not read saved PAT.");
        }

        if (string.IsNullOrWhiteSpace(pat))
        {
            return AzureFacetComposer.AuthRequired(DateTimeOffset.UtcNow, focusBranch, "Authentication required");
        }

        var adoProject = !string.IsNullOrWhiteSpace(azure.AdoProjectId)
            ? azure.AdoProjectId
            : azure.AdoProjectName;
        var displayRepresentatives = new List<AzurePipelineRunInfo>();
        var healthRepresentatives = new List<AzurePipelineRunInfo>();
        var extraAttention = new List<AzurePipelineRunInfo>();

        foreach (var pipeline in azure.Pipelines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relevant = AzureRelevantBranchSet.Build(azure, focusBranch, pipeline.DefinitionId);
            var result = await pollClient.ListRecentBuildsAsync(
                connection.OrganizationUrl,
                adoProject,
                pipeline.DefinitionId,
                string.IsNullOrWhiteSpace(pipeline.DisplayName)
                    ? $"Pipeline {pipeline.DefinitionId}"
                    : pipeline.DisplayName,
                pat,
                cancellationToken).ConfigureAwait(false);

            if (result.Outcome == AzureBuildPollOutcome.PatMissing
                || result.Outcome == AzureBuildPollOutcome.AuthRequired)
            {
                return AzureFacetComposer.AuthRequired(
                    DateTimeOffset.UtcNow,
                    focusBranch,
                    result.Message ?? "Authentication required");
            }

            if (result.Outcome == AzureBuildPollOutcome.Unavailable)
            {
                return AzureFacetComposer.Unavailable(
                    DateTimeOffset.UtcNow,
                    focusBranch,
                    result.Message ?? "Azure DevOps unavailable");
            }

            var display = AzureRunSelector.SelectDisplayRepresentative(result.Runs);
            if (display is not null)
            {
                displayRepresentatives.Add(display);
                var previousFailure = AzureRunSelector.SelectPreviousFailureAttention(result.Runs, display);
                if (previousFailure is not null)
                {
                    extraAttention.Add(previousFailure);
                }
            }

            var health = AzureRunSelector.SelectHealthRepresentative(result.Runs, relevant);
            if (health is not null)
            {
                healthRepresentatives.Add(health);
            }
        }

        var facet = AzureFacetComposer.FromPipelineRuns(
            azure,
            displayRepresentatives,
            focusBranch,
            DateTimeOffset.UtcNow,
            healthRepresentatives,
            extraAttention,
            new AzureBuildNavigationContext(
                project.Id,
                azure.ConnectionId,
                connection.OrganizationUrl,
                adoProject,
                azure.RepositoryName,
                azure.RepositoryId));

        return await AttachActiveRunExecutionAsync(
            project.Id,
            facet,
            connection.OrganizationUrl,
            adoProject,
            pat,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches Builds timeline only for an active primary run. Never invents stage state on failure.
    /// Discards results that no longer match the facet primary RunId.
    /// </summary>
    internal async Task<ProjectAzureHealthFacet> AttachActiveRunExecutionAsync(
        string projectId,
        ProjectAzureHealthFacet facet,
        string organizationUrl,
        string adoProjectIdOrName,
        string pat,
        CancellationToken cancellationToken)
    {
        var primary = facet.PrimaryRun;
        if (primary is null || !AzureRunSelector.IsActive(primary.State))
        {
            timelineCache.TryRemove(projectId, out _);
            timelineRequestRunId.TryRemove(projectId, out _);
            return facet.ExecutionDetail is null ? facet : facet with { ExecutionDetail = null };
        }

        var requestedRunId = primary.RunId;
        timelineRequestRunId[projectId] = requestedRunId;
        if (timelineCache.TryGetValue(projectId, out var cached) && cached.RunId != requestedRunId)
        {
            timelineCache.TryRemove(projectId, out _);
        }

        AzureBuildTimelineResult timeline;
        try
        {
            timeline = await timelineClient.GetTimelineAsync(
                organizationUrl,
                adoProjectIdOrName,
                requestedRunId,
                pat,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            timelineCache.TryRemove(projectId, out _);
            return facet with { ExecutionDetail = null };
        }

        // Stale-run guard: a newer primary attach superseded this in-flight timeline.
        if (!timelineRequestRunId.TryGetValue(projectId, out var latestRequested)
            || latestRequested != requestedRunId
            || !AzureRunSelector.IsActive(facet.PrimaryRun!.State)
            || facet.PrimaryRun.RunId != requestedRunId)
        {
            timelineCache.TryRemove(projectId, out _);
            return facet with { ExecutionDetail = null };
        }

        if (timeline.Outcome != AzureBuildTimelineOutcome.Ok)
        {
            return facet with { ExecutionDetail = null };
        }

        if (timelineCache.TryGetValue(projectId, out cached)
            && cached.RunId == requestedRunId
            && cached.ChangeId is not null
            && cached.ChangeId == timeline.ChangeId
            && cached.Detail is not null)
        {
            return facet with { ExecutionDetail = cached.Detail };
        }

        var detail = AzureRunExecutionProjector.TryCreateDetail(requestedRunId, timeline);
        if (detail is null || detail.RunId != requestedRunId)
        {
            timelineCache.TryRemove(projectId, out _);
            return facet with { ExecutionDetail = null };
        }

        timelineCache[projectId] = new TimelineCacheEntry(requestedRunId, timeline.ChangeId, detail);
        return facet with { ExecutionDetail = detail };
    }

    public static IReadOnlyList<MonitoredProjectSettings> GetEligibleProjects(AppSettings settings) =>
        settings.Projects
            .Where(p => p.IsActiveInSession
                && p.Azure is not null
                && p.Azure.Pipelines.Count > 0
                && settings.Connections.Any(c =>
                    string.Equals(c.Id, p.Azure.ConnectionId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    private static TimeSpan FailureBackoff(int streak)
    {
        var seconds = FailureBackoffInitial.TotalSeconds * Math.Pow(2, Math.Max(0, streak - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, FailureBackoffMax.TotalSeconds));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
