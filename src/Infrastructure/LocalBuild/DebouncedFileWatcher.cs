namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly System.Timers.Timer debounceTimer;
    private readonly HashSet<string> ignoreSegments;

    public event Action<IReadOnlyList<string>>? Changed;

    private readonly object pendingPathsSync = new();
    private readonly HashSet<string> pendingPaths = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSuspended { get; private set; }

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
            pendingPaths.Add(e.FullPath);
        }

        debounceTimer.Stop();
        debounceTimer.Start();
    }

    private void RaiseChanged()
    {
        List<string> snapshot;
        lock (pendingPathsSync)
        {
            snapshot = pendingPaths.ToList();
            pendingPaths.Clear();
        }

        var meaningful = WatchIgnoreRules.FilterMeaningfulPaths(snapshot, ignoreSegments);
        if (meaningful.Count == 0)
        {
            return;
        }

        Changed?.Invoke(meaningful);
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
