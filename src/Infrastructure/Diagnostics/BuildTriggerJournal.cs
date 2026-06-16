using System.Text.Json;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

public sealed class BuildTriggerJournal
{
    private const int MaxEntries = 500;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly object sync = new();
    private readonly List<BuildTriggerRecord> entries = [];
    private readonly string journalPath;

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
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            AppendLine(entry);
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
            return entries.ToList();
        }
    }

    private void LoadRecent()
    {
        if (!File.Exists(journalPath))
        {
            return;
        }

        try
        {
            foreach (var line in File.ReadLines(journalPath).TakeLast(MaxEntries))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize<BuildTriggerRecord>(line, JsonOptions);
                if (record is not null)
                {
                    entries.Insert(0, record);
                }
            }
        }
        catch
        {
            entries.Clear();
        }
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
