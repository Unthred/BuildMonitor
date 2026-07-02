namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly System.Timers.Timer debounceTimer;
    private readonly HashSet<string> ignoreSegments;

    public event Action<IReadOnlyList<string>, int>? Changed;

    private readonly object pendingPathsSync = new();
    private readonly HashSet<string> pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? burstStartedUtc;

    public bool IsSuspended { get; private set; }

    public bool HasPendingChanges
    {
        get
        {
            lock (pendingPathsSync)
            {
                return pendingPaths.Count > 0 || debounceTimer.Enabled;
            }
        }
    }

    public DateTimeOffset? BurstStartedUtc
    {
        get
        {
            lock (pendingPathsSync)
            {
                return burstStartedUtc;
            }
        }
    }

    public DebouncedFileWatcher(string rootPath, int debounceMs, IEnumerable<string>? extraIgnoreSegments = null)
    {
        ignoreSegments = new HashSet<string>(
            WatchExcludeSegments.DefaultSegmentSet,
            StringComparer.OrdinalIgnoreCase);
        if (extraIgnoreSegments is not null)
        {
            foreach (var segment in extraIgnoreSegments)
            {
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    ignoreSegments.Add(segment);
                }
            }
        }

        watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        debounceTimer = new System.Timers.Timer(debounceMs) { AutoReset = false };
        debounceTimer.Elapsed += (_, _) => RaiseChanged();

        watcher.Changed += OnFsEvent;
        watcher.Created += OnFsEvent;
        watcher.Renamed += OnFsEvent;
        watcher.EnableRaisingEvents = true;
    }

    public void SetDebounceMs(int debounceMs)
    {
        debounceTimer.Interval = Math.Max(1, debounceMs);
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (IsSuspended)
        {
            return;
        }

        if (ShouldIgnorePath(e.FullPath))
        {
            return;
        }

        lock (pendingPathsSync)
        {
            if (pendingPaths.Count == 0)
            {
                burstStartedUtc = DateTimeOffset.UtcNow;
            }

            pendingPaths.Add(e.FullPath);
        }

        debounceTimer.Stop();
        debounceTimer.Start();
    }

    private void RaiseChanged()
    {
        List<string> snapshot;
        int burstDurationMs;
        lock (pendingPathsSync)
        {
            snapshot = pendingPaths.ToList();
            pendingPaths.Clear();
            burstDurationMs = burstStartedUtc is { } started
                ? (int)Math.Max(0, (DateTimeOffset.UtcNow - started).TotalMilliseconds)
                : 0;
            burstStartedUtc = null;
        }

        var meaningful = WatchIgnoreRules.FilterMeaningfulPaths(snapshot, ignoreSegments);
        if (meaningful.Count == 0)
        {
            return;
        }

        Changed?.Invoke(meaningful, burstDurationMs);
    }

    public void Suspend() => IsSuspended = true;

    public void Resume() => IsSuspended = false;

    private bool ShouldIgnorePath(string path) =>
        WatchIgnoreRules.ShouldIgnorePath(path, ignoreSegments);

    public void Dispose()
    {
        watcher.Dispose();
        debounceTimer.Dispose();
    }
}
