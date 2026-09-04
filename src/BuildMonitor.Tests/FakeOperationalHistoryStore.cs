using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Tests;

internal sealed class FakeOperationalHistoryStore : IOperationalHistoryStore
{
    private readonly List<OperationalEvent> events = [];
    private readonly object sync = new();
    public bool ThrowOnRecord { get; set; }
    public bool ReturnFalseOnRecord { get; set; }

    public IReadOnlyList<OperationalEvent> Events
    {
        get
        {
            lock (sync)
            {
                return events.ToList();
            }
        }
    }

    public bool TryRecord(OperationalEvent entry)
    {
        if (ThrowOnRecord)
        {
            throw new InvalidOperationException("forced history failure");
        }

        if (ReturnFalseOnRecord)
        {
            return false;
        }

        lock (sync)
        {
            events.Insert(0, entry);
        }

        return true;
    }

    public IReadOnlyList<OperationalEvent> GetRecent(int? limit = null)
    {
        lock (sync)
        {
            return limit is null ? events.ToList() : events.Take(limit.Value).ToList();
        }
    }

    public IReadOnlyList<OperationalEvent> GetRecentForProject(string projectId, int? limit = null)
    {
        lock (sync)
        {
            var filtered = events
                .Where(e => string.Equals(e.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return limit is null ? filtered : filtered.Take(limit.Value).ToList();
        }
    }

    public IReadOnlyList<OperationalEvent> Chronological()
    {
        lock (sync)
        {
            return events.AsEnumerable().Reverse().ToList();
        }
    }
}
