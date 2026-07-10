namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>
/// Watches agent tooling folders for activity signals only — never triggers rebuilds.
/// </summary>
public sealed class AgentActivityWatcher : IDisposable
{
    private static readonly string[] ActivitySegments =
    [
        "agent-transcripts",
        ".cursor"
    ];

    private readonly FileSystemWatcher watcher;
    private readonly object sync = new();
    private DateTimeOffset lastActivityUtc = DateTimeOffset.MinValue;
    private bool isSuspended;

    public AgentActivityWatcher(string rootPath)
    {
        watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        watcher.Changed += OnFsEvent;
        watcher.Created += OnFsEvent;
        watcher.Renamed += OnFsEvent;
        watcher.EnableRaisingEvents = true;
    }

    public event Action? ActivityDetected;

    public DateTimeOffset LastActivityUtc
    {
        get
        {
            lock (sync)
            {
                return lastActivityUtc;
            }
        }
    }

    public void Suspend() => isSuspended = true;

    public void Resume() => isSuspended = false;

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (isSuspended || !IsAgentActivityPath(e.FullPath))
        {
            return;
        }

        lock (sync)
        {
            lastActivityUtc = DateTimeOffset.UtcNow;
        }

        ActivityDetected?.Invoke();
    }

    internal static bool IsAgentActivityPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var parts = fullPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in ActivitySegments)
        {
            if (parts.Any(p => string.Equals(p, segment, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose() => watcher.Dispose();
}
