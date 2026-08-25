using System.Threading.Channels;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Infrastructure.Services;

/// <summary>
/// Background coalescer for project health snapshots. Parses build output and publishes
/// tray health at a bounded rate so the UI thread is not flooded during large builds.
/// Merges optional Azure facets into the same snapshot stream (no parallel tray publisher).
/// </summary>
internal sealed class HealthCoalescer : IDisposable
{
    private const int CoalesceIntervalMs = 250;

    private readonly Func<(IReadOnlyList<ProjectRuntime> Runtimes, IReadOnlyList<MonitoredProjectSettings> Projects)> getState;
    private readonly Func<string, ProjectAzureHealthFacet?> getAzureFacet;
    private readonly Action<IReadOnlyList<ProjectHealthSnapshot>, MonitorHealth> publish;
    private readonly Channel<bool> wakeChannel = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource disposeCts = new();
    private readonly Task loopTask;
    private readonly object cacheSync = new();
    private readonly Dictionary<string, ProjectHealthSnapshot> localSnapshotCache = new(StringComparer.OrdinalIgnoreCase);

    private int trayMenuOpen;
    private int pendingPublish;

    public HealthCoalescer(
        Func<(IReadOnlyList<ProjectRuntime> Runtimes, IReadOnlyList<MonitoredProjectSettings> Projects)> getState,
        Func<string, ProjectAzureHealthFacet?> getAzureFacet,
        Action<IReadOnlyList<ProjectHealthSnapshot>, MonitorHealth> publish)
    {
        this.getState = getState;
        this.getAzureFacet = getAzureFacet;
        this.publish = publish;
        WorkerHealthRegistry.Shared.Register(
            "health.coalescer",
            "Health coalescer loop",
            TimeSpan.FromMilliseconds(CoalesceIntervalMs * 3),
            "Background");
        WorkerHealthRegistry.Shared.Register(
            "health.coalescer.publish",
            "Health publish to tray",
            TimeSpan.FromMilliseconds(CoalesceIntervalMs * 3),
            "Background");
        WorkerHealthRegistry.Shared.SetCurrentAction("health.coalescer", "Idle");
        loopTask = Task.Run(RunLoopAsync);
    }

    public void Request(bool immediate = false) => wakeChannel.Writer.TryWrite(immediate);

    public void SetTrayMenuOpen(bool open)
    {
        var wasOpen = Interlocked.Exchange(ref trayMenuOpen, open ? 1 : 0) != 0;
        if (wasOpen && !open)
        {
            Request(immediate: true);
        }
    }

    public IReadOnlyList<ProjectHealthSnapshot> GetSnapshots()
    {
        var (runtimes, projects) = getState();
        lock (cacheSync)
        {
            RefreshLocalCache(runtimes, projects, forceRuntime: false);
            return BuildMergedList(runtimes, projects);
        }
    }

    public void Dispose()
    {
        disposeCts.Cancel();
        wakeChannel.Writer.TryComplete();
        try
        {
            loopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore shutdown races
        }

        disposeCts.Dispose();
    }

    private async Task RunLoopAsync()
    {
        var reader = wakeChannel.Reader;
        var token = disposeCts.Token;

        while (!token.IsCancellationRequested)
        {
            var immediate = false;
            try
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                delayCts.CancelAfter(CoalesceIntervalMs);

                if (await reader.WaitToReadAsync(delayCts.Token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var item))
                    {
                        immediate |= item;
                    }
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // periodic wake
            }
            catch (OperationCanceledException)
            {
                break;
            }

            CoalesceAndMaybePublish(immediate);
            WorkerHealthRegistry.Shared.Heartbeat(
                "health.coalescer",
                managedThreadId: Environment.CurrentManagedThreadId);
        }
    }

    private void CoalesceAndMaybePublish(bool immediate)
    {
        var (runtimes, projects) = getState();
        var anyCoalesced = false;

        foreach (var runtime in runtimes)
        {
            var coalesced = immediate
                ? ForceCoalesce(runtime)
                : runtime.TryCoalesceHealth();

            if (!coalesced && !immediate)
            {
                continue;
            }

            if (coalesced)
            {
                anyCoalesced = true;
            }

            lock (cacheSync)
            {
                localSnapshotCache[runtime.ProjectId] = runtime.BuildSnapshot();
            }
        }

        lock (cacheSync)
        {
            RefreshLocalCache(runtimes, projects, forceRuntime: immediate);
        }

        if (!anyCoalesced && !immediate)
        {
            if (Volatile.Read(ref pendingPublish) == 0)
            {
                WorkerHealthRegistry.Shared.SetCurrentAction("health.coalescer", "Idle");
                return;
            }
        }

        if (Volatile.Read(ref trayMenuOpen) != 0)
        {
            Volatile.Write(ref pendingPublish, 1);
            WorkerHealthRegistry.Shared.SetCurrentAction("health.coalescer", "Idle");
            return;
        }

        Volatile.Write(ref pendingPublish, 0);

        IReadOnlyList<ProjectHealthSnapshot> snapshots;
        lock (cacheSync)
        {
            snapshots = BuildMergedList(runtimes, projects);
        }

        var activeOnly = snapshots.Where(s => s.IsActive).ToList();
        var rollup = LocalTrayIconRollupEvaluator.Rollup(activeOnly);
        WorkerHealthRegistry.Shared.SetCurrentAction(
            "health.coalescer",
            $"Publishing tray health ({activeOnly.Count} active)");
        publish(snapshots, rollup);
        WorkerHealthRegistry.Shared.Heartbeat(
            "health.coalescer.publish",
            note: $"{activeOnly.Count} active",
            managedThreadId: Environment.CurrentManagedThreadId);
        WorkerHealthRegistry.Shared.SetCurrentAction("health.coalescer", "Idle");
    }

    private static bool ForceCoalesce(ProjectRuntime runtime)
    {
        runtime.ForceCoalesceHealth();
        return true;
    }

    private void RefreshLocalCache(
        IReadOnlyList<ProjectRuntime> runtimes,
        IReadOnlyList<MonitoredProjectSettings> projects,
        bool forceRuntime)
    {
        var runtimeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var runtime in runtimes)
        {
            runtimeIds.Add(runtime.ProjectId);
            if (forceRuntime || !localSnapshotCache.ContainsKey(runtime.ProjectId))
            {
                localSnapshotCache[runtime.ProjectId] = runtime.BuildSnapshot();
            }
        }

        foreach (var project in projects)
        {
            if (runtimeIds.Contains(project.Id))
            {
                continue;
            }

            localSnapshotCache[project.Id] = BuildNonRuntimeSnapshot(project);
        }
    }

    private List<ProjectHealthSnapshot> BuildMergedList(
        IReadOnlyList<ProjectRuntime> runtimes,
        IReadOnlyList<MonitoredProjectSettings> projects)
    {
        var list = new List<ProjectHealthSnapshot>(projects.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var runtime in runtimes)
        {
            if (!localSnapshotCache.TryGetValue(runtime.ProjectId, out var local))
            {
                local = runtime.BuildSnapshot();
            }

            list.Add(ProjectHealthComposer.WithAzure(local with { Azure = null }, getAzureFacet(runtime.ProjectId)));
            seen.Add(runtime.ProjectId);
        }

        foreach (var project in projects)
        {
            if (!seen.Add(project.Id))
            {
                continue;
            }

            if (!localSnapshotCache.TryGetValue(project.Id, out var local))
            {
                local = BuildNonRuntimeSnapshot(project);
            }

            list.Add(ProjectHealthComposer.WithAzure(local with { Azure = null }, getAzureFacet(project.Id)));
        }

        return list;
    }

    private static ProjectHealthSnapshot BuildNonRuntimeSnapshot(MonitoredProjectSettings project)
    {
        var isAzureOnlyActive = project is { IsActiveInSession: true, Local: null, Azure: not null };
        return new ProjectHealthSnapshot(
            project.Id,
            project.DisplayName,
            MonitorHealth.Unknown,
            ProjectHealthEvaluator.ToLabel(MonitorHealth.Unknown),
            ProjectLifecycleState.Idle,
            null,
            null,
            null,
            0,
            0,
            isAzureOnlyActive ? DateTimeOffset.UtcNow : DateTimeOffset.MinValue,
            null,
            isAzureOnlyActive,
            [],
            null,
            false,
            false);
    }
}
