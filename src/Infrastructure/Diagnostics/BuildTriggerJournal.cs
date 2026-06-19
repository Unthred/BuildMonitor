using System.Text.Json;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

public sealed class BuildTriggerJournal
{
    private const int MaxEntriesPerDay = 500;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly object sync = new();
    private readonly List<BuildTriggerRecord> entries = [];
    private readonly string journalPath;
    private DateTime retainedLocalDate = DateTime.MinValue;

    public BuildTriggerJournal(string appDataDirectory)
    {
        var dir = Path.Combine(appDataDirectory, "diagnostics");
        Directory.CreateDirectory(dir);
        journalPath = Path.Combine(dir, "build-triggers.jsonl");
        LoadRecent();
    }

    public event Action? Changed;

    public void Record(BuildTriggerRecord entry)
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

    public void SetVerdict(string id, BuildTriggerVerdict verdict)
    {
        lock (sync)
        {
            var index = entries.FindIndex(e => e.Id == id);
            if (index < 0)
            {
                return;
            }

            var existing = entries[index];
            entries[index] = existing with { Verdict = verdict };
            RewriteJournal();
        }

        Changed?.Invoke();
    }

    public void SetUserNote(string id, string? userNote)
    {
        lock (sync)
        {
            var index = entries.FindIndex(e => e.Id == id);
            if (index < 0)
            {
                return;
            }

            var existing = entries[index];
            entries[index] = existing with { UserNote = string.IsNullOrWhiteSpace(userNote) ? null : userNote.Trim() };
            RewriteJournal();
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<BuildTriggerRecord> GetEntries()
    {
        lock (sync)
        {
            PruneEntriesNotFromToday();
            return entries.ToList();
        }
    }

    internal static bool IsTodayLocal(DateTimeOffset occurredAtUtc) =>
        occurredAtUtc.ToLocalTime().Date == DateTime.Today;

    private void LoadRecent()
    {
        if (!File.Exists(journalPath))
        {
            retainedLocalDate = DateTime.Today;
            return;
        }

        try
        {
            var loaded = new List<BuildTriggerRecord>();
            var droppedOlder = false;
            foreach (var line in File.ReadLines(journalPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize<BuildTriggerRecord>(line, JsonOptions);
                if (record is null)
                {
                    continue;
                }

                if (IsTodayLocal(record.OccurredAtUtc))
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
        entries.RemoveAll(e => !IsTodayLocal(e.OccurredAtUtc));
        return dayChanged || entries.Count != before;
    }

    private void AppendLine(BuildTriggerRecord entry)
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
