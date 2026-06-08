namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly System.Timers.Timer debounceTimer;
    private readonly HashSet<string> ignoreSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "node_modules"
    };

    public event Action? Changed;

    public bool IsSuspended { get; private set; }

    public DebouncedFileWatcher(string rootPath, int debounceMs)
    {
        watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        debounceTimer = new System.Timers.Timer(debounceMs) { AutoReset = false };
        debounceTimer.Elapsed += (_, _) => Changed?.Invoke();

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

        if (ShouldIgnore(e.FullPath))
        {
            return;
        }

        debounceTimer.Stop();
        debounceTimer.Start();
    }

    public void Suspend() => IsSuspended = true;

    public void Resume() => IsSuspended = false;

    private bool ShouldIgnore(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => ignoreSegments.Contains(p));
    }

    public void Dispose()
    {
        watcher.Dispose();
        debounceTimer.Dispose();
    }
}
