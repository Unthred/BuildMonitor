using System.Threading.Channels;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Infrastructure.Services;

/// <summary>
/// Background coalescer for project health snapshots. Parses build output and publishes
/// tray health at a bounded rate so the UI thread is not flooded during large builds.
/// </summary>
internal sealed class HealthCoalescer : IDisposable
{
    private const int CoalesceIntervalMs = 250;

    private readonly Func<(IReadOnlyList<ProjectRuntime> Runtimes, IReadOnlyList<LocalProjectDefinition> Inactive)> getState;
    private readonly Action<IReadOnlyList<ProjectHealthSnapshot>, MonitorHealth> publish;
    private readonly Channel<bool> wakeChannel = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource disposeCts = new();
    private readonly Task loopTask;
    private readonly object cacheSync = new();
    private readonly Dictionary<string, ProjectHealthSnapshot> snapshotCache = new(StringComparer.OrdinalIgnoreCase);

    private int trayMenuOpen;
    private int pendingPublish;

    public HealthCoalescer(
        Func<(IReadOnlyList<ProjectRuntime> Runtimes, IReadOnlyList<LocalProjectDefinition> Inactive)> getState,
        Action<IReadOnlyList<ProjectHealthSnapshot>, MonitorHealth> publish)
    {
        this.getState = getState;
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

    public void Request(bool immediate = false)
    {
        wakeChannel.Writer.TryWrite(immediate);
    }

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
        var (runtimes, inactive) = getState();
        lock (cacheSync)
        {
            EnsureInactiveInCache(inactive);
            return BuildSnapshotList(runtimes, inactive);
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
                // periodic wake without an explicit signal
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
        var (runtimes, inactive) = getState();
        var anyCoalesced = false;

        foreach (var runtime in runtimes)
        {
            var coalesced = immediate
                ? CoalesceImmediate(runtime)
                : runtime.TryCoalesceHealth();

            if (!coalesced)
            {
                continue;
            }

            anyCoalesced = true;
            lock (cacheSync)
            {
                snapshotCache[runtime.ProjectId] = runtime.BuildSnapshot();
            }
        }

        lock (cacheSync)
        {
            EnsureInactiveInCache(inactive);
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
            snapshots = BuildSnapshotList(runtimes, inactive);
        }

        var activeOnly = snapshots.Where(s => s.IsActive).ToList();
        var rollup = LocalTrayIconRollupEvaluator.Rollup(activeOnly);
        if (anyCoalesced)
        {
            WorkerHealthRegistry.Shared.SetCurrentAction("health.coalescer", "Coalescing project health");
        }

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

    private static bool CoalesceImmediate(ProjectRuntime runtime)
    {
        runtime.ForceCoalesceHealth();
        return true;
    }

    private void EnsureInactiveInCache(IReadOnlyList<LocalProjectDefinition> inactive)
    {
        foreach (var project in inactive)
        {
            snapshotCache[project.Id] = new ProjectHealthSnapshot(
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
                DateTimeOffset.MinValue,
                null,
                false,
                [],
                null,
                false,
                project.RunOptions.RunMode != ProjectRunMode.None);
        }
    }

    private List<ProjectHealthSnapshot> BuildSnapshotList(
        IReadOnlyList<ProjectRuntime> runtimes,
        IReadOnlyList<LocalProjectDefinition> inactive)
    {
        var list = new List<ProjectHealthSnapshot>(runtimes.Count + inactive.Count);
        foreach (var runtime in runtimes)
        {
            if (snapshotCache.TryGetValue(runtime.ProjectId, out var snapshot))
            {
                list.Add(snapshot);
            }
            else
            {
                list.Add(runtime.BuildSnapshot());
            }
        }

        foreach (var project in inactive)
        {
            if (snapshotCache.TryGetValue(project.Id, out var snapshot))
            {
                list.Add(snapshot);
            }
        }

        return list;
    }
}
