using System.Text.Json;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

public sealed class ControlPlaneEventJournal
{
    private const int MaxEntriesPerDay = 300;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly object sync = new();
    private readonly List<ControlPlaneEventRecord> entries = [];
    private readonly string journalPath;
    private DateTime retainedLocalDate = DateTime.MinValue;

    public ControlPlaneEventJournal(string appDataDirectory)
    {
        var dir = Path.Combine(appDataDirectory, "diagnostics");
        Directory.CreateDirectory(dir);
        journalPath = Path.Combine(dir, "control-plane-events.jsonl");
        LoadRecent();
    }

    public event Action? Changed;

    public void Record(ControlPlaneEventRecord entry)
    {
        lock (sync)
        {
            var compacted = PruneEntriesNotFromToday();
            entries.Insert(0, entry);
            if (entries.Count > MaxEntriesPerDay)
            {
                entries.RemoveRange(MaxEntriesPerDay, entries.Count - MaxEntriesPerDay);
            }

            if (compacted)
            {
                RewriteJournal();
            }
            else
            {
                AppendLine(entry);
            }
        }

        Changed?.Invoke();
    }

    public void Record(
        string projectId,
        ControlPlaneEventKind kind,
        string summary,
        string? detail = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        Record(new ControlPlaneEventRecord(
            Guid.NewGuid().ToString("N"),
            projectId,
            occurredAtUtc ?? DateTimeOffset.UtcNow,
            kind,
            summary,
            detail));
    }

    public IReadOnlyList<ControlPlaneEventRecord> GetEntries()
    {
        lock (sync)
        {
            PruneEntriesNotFromToday();
            return entries.ToList();
        }
    }

    private void LoadRecent()
    {
        if (!File.Exists(journalPath))
        {
            retainedLocalDate = DateTime.Today;
            return;
        }

        try
        {
            var loaded = new List<ControlPlaneEventRecord>();
            var droppedOlder = false;
            foreach (var line in File.ReadLines(journalPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize<ControlPlaneEventRecord>(line, JsonOptions);
                if (record is null)
                {
                    continue;
                }

                if (BuildTriggerJournal.IsTodayLocal(record.OccurredAtUtc))
                {
                    loaded.Add(record);
                }
                else
                {
                    droppedOlder = true;
                }
            }

            entries.Clear();
            entries.AddRange(loaded.OrderByDescending(e => e.OccurredAtUtc));
            retainedLocalDate = DateTime.Today;

            if (droppedOlder)
            {
                RewriteJournal();
            }
        }
        catch
        {
            entries.Clear();
            retainedLocalDate = DateTime.Today;
        }
    }

    private bool PruneEntriesNotFromToday()
    {
        var today = DateTime.Today;
        var dayChanged = retainedLocalDate != today;
        retainedLocalDate = today;

        var before = entries.Count;
        entries.RemoveAll(e => !BuildTriggerJournal.IsTodayLocal(e.OccurredAtUtc));
        return dayChanged || entries.Count != before;
    }

    private void AppendLine(ControlPlaneEventRecord entry)
    {
        try
        {
            File.AppendAllText(journalPath, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
        catch
        {
            // Best effort only.
        }
    }

    private void RewriteJournal()
    {
        try
        {
            var lines = entries
                .OrderBy(e => e.OccurredAtUtc)
                .Select(e => JsonSerializer.Serialize(e, JsonOptions));
            File.WriteAllLines(journalPath, lines);
        }
        catch
        {
            // Best effort only.
        }
    }
}
