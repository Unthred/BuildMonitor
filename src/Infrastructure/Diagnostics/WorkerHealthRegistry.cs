namespace BuildMonitor.Infrastructure.Diagnostics;

public enum WorkerHealthState
{
    Ok,
    Stale,
    Unresponsive
}

public sealed record WorkerHealthSnapshot(
    string Id,
    string DisplayName,
    string? Category,
    int? ManagedThreadId,
    DateTimeOffset LastHeartbeatUtc,
    long HeartbeatCount,
    long? LastWorkDurationMs,
    string? LastNote,
    string? CurrentAction,
    WorkerHealthState State,
    TimeSpan Age,
    long TimeoutCount);

/// <summary>
/// Lightweight heartbeat registry for background workers and UI callbacks.
/// Used by the thread-health debug window to spot stalled loops and a blocked WPF dispatcher.
/// </summary>
public sealed class WorkerHealthRegistry
{
    public static WorkerHealthRegistry Shared { get; } = new();

    private readonly object sync = new();
    private readonly Dictionary<string, WorkerEntry> entries = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string id, string displayName, TimeSpan staleAfter, string? category = null)
    {
        lock (sync)
        {
            entries[id] = new WorkerEntry
            {
                Id = id,
                DisplayName = displayName,
                Category = category,
                StaleAfter = staleAfter,
                LastHeartbeatUtc = DateTimeOffset.MinValue
            };
        }
    }

    public void Unregister(string id)
    {
        lock (sync)
        {
            entries.Remove(id);
        }
    }

    public void SetCurrentAction(string id, string? action)
    {
        lock (sync)
        {
            if (!entries.TryGetValue(id, out var entry))
            {
                return;
            }

            entry.CurrentAction = string.IsNullOrWhiteSpace(action) ? null : action.Trim();
        }
    }

    public void Heartbeat(
        string id,
        string? note = null,
        int? managedThreadId = null,
        long? workDurationMs = null)
    {
        lock (sync)
        {
            if (!entries.TryGetValue(id, out var entry))
            {
                return;
            }

            entry.LastHeartbeatUtc = DateTimeOffset.UtcNow;
            entry.HeartbeatCount++;
            if (note is not null)
            {
                entry.LastNote = note;
            }

            if (managedThreadId is not null)
            {
                entry.LastManagedThreadId = managedThreadId;
            }

            if (workDurationMs is not null)
            {
                entry.LastWorkDurationMs = workDurationMs.Value;
            }
        }
    }

    public void RecordTimeout(string id)
    {
        lock (sync)
        {
            if (!entries.TryGetValue(id, out var entry))
            {
                return;
            }

            entry.TimeoutCount++;
            entry.LastNote = $"dispatcher ping timed out (total {entry.TimeoutCount})";
        }
    }

    public IReadOnlyList<WorkerHealthSnapshot> GetSnapshots(DateTimeOffset? now = null)
    {
        var utcNow = now ?? DateTimeOffset.UtcNow;

        lock (sync)
        {
            return entries.Values
                .Select(entry => ToSnapshot(entry, utcNow))
                .OrderBy(s => s.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static WorkerHealthSnapshot ToSnapshot(WorkerEntry entry, DateTimeOffset utcNow)
    {
        var age = entry.LastHeartbeatUtc == DateTimeOffset.MinValue
            ? TimeSpan.MaxValue
            : utcNow - entry.LastHeartbeatUtc;

        var state = entry.TimeoutCount > 0 && age > entry.StaleAfter
            ? WorkerHealthState.Unresponsive
            : age > entry.StaleAfter
                ? WorkerHealthState.Stale
                : WorkerHealthState.Ok;

        if (entry.LastHeartbeatUtc == DateTimeOffset.MinValue)
        {
            state = WorkerHealthState.Stale;
        }

        return new WorkerHealthSnapshot(
            entry.Id,
            entry.DisplayName,
            entry.Category,
            entry.LastManagedThreadId,
            entry.LastHeartbeatUtc,
            entry.HeartbeatCount,
            entry.LastWorkDurationMs,
            entry.LastNote,
            entry.CurrentAction,
            state,
            age == TimeSpan.MaxValue ? TimeSpan.Zero : age,
            entry.TimeoutCount);
    }

    private sealed class WorkerEntry
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public string? Category { get; init; }
        public required TimeSpan StaleAfter { get; init; }
        public DateTimeOffset LastHeartbeatUtc { get; set; }
        public long HeartbeatCount { get; set; }
        public int? LastManagedThreadId { get; set; }
        public long? LastWorkDurationMs { get; set; }
        public string? LastNote { get; set; }
        public string? CurrentAction { get; set; }
        public long TimeoutCount { get; set; }
    }
}
