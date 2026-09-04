using System.Text.Json;
using System.Text.Json.Serialization;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

/// <summary>
/// Hybrid in-memory + JSONL operational history store.
/// File: %LocalAppData%/BuildMonitor/diagnostics/operational-history.jsonl (or custom directory).
/// </summary>
public sealed class OperationalHistoryStore : IOperationalHistoryStore
{
    public const int DefaultMaxAgeDays = 3;
    public const int DefaultMaxEventsPerProject = 250;
    public const string FileName = "operational-history.jsonl";
    public const string CorruptTailFileName = "operational-history.corrupt-tail.txt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object sync = new();
    private readonly List<OperationalEvent> entries = [];
    private readonly HashSet<string> knownIds = new(StringComparer.Ordinal);
    private readonly string journalPath;
    private readonly string corruptTailPath;
    private readonly TimeSpan maxAge;
    private readonly int maxEventsPerProject;
    private readonly Action<string>? onPersistenceWarning;

    public OperationalHistoryStore(
        string appDataDirectory,
        TimeSpan? maxAge = null,
        int maxEventsPerProject = DefaultMaxEventsPerProject,
        Action<string>? onPersistenceWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        if (maxEventsPerProject < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEventsPerProject));
        }

        var dir = Path.Combine(appDataDirectory, "diagnostics");
        Directory.CreateDirectory(dir);
        journalPath = Path.Combine(dir, FileName);
        corruptTailPath = Path.Combine(dir, CorruptTailFileName);
        this.maxAge = maxAge ?? TimeSpan.FromDays(DefaultMaxAgeDays);
        this.maxEventsPerProject = maxEventsPerProject;
        this.onPersistenceWarning = onPersistenceWarning;
        LoadRecent();
    }

    /// <summary>Test/diagnostic: absolute path of the JSONL journal.</summary>
    public string JournalPath => journalPath;

    public bool TryRecord(OperationalEvent entry)
    {
        if (!IsValid(entry))
        {
            return false;
        }

        entry = Normalize(entry);

        lock (sync)
        {
            if (!knownIds.Add(entry.Id))
            {
                return false;
            }

            // Memory is authoritative for the current process; disk is best-effort durability.
            entries.Insert(0, entry);
            var compacted = ApplyRetentionLocked(DateTimeOffset.UtcNow);
            if (compacted)
            {
                RewriteJournalLocked();
            }
            else
            {
                AppendLineLocked(entry);
            }

            // true = accepted into session history (may still be lost on restart if disk failed).
            return true;
        }
    }

    public IReadOnlyList<OperationalEvent> GetRecent(int? limit = null)
    {
        lock (sync)
        {
            ApplyRetentionLocked(DateTimeOffset.UtcNow);
            return TakeNewest(entries, limit);
        }
    }

    public IReadOnlyList<OperationalEvent> GetRecentForProject(string projectId, int? limit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        lock (sync)
        {
            ApplyRetentionLocked(DateTimeOffset.UtcNow);
            var filtered = entries.Where(e =>
                e.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase));
            return TakeNewest(filtered, limit);
        }
    }

    private static bool IsValid(OperationalEvent entry) =>
        entry.SchemaVersion == OperationalHistorySchema.CurrentVersion
        && !string.IsNullOrWhiteSpace(entry.Id)
        && !string.IsNullOrWhiteSpace(entry.ProjectId)
        && !string.IsNullOrWhiteSpace(entry.Summary)
        && entry.OccurredAtUtc != default;

    private static OperationalEvent Normalize(OperationalEvent entry)
    {
        var names = entry.Detail?.FailingTestNames;
        if (names is null || names.Count <= OperationalEventDetail.MaxFailingTestNames)
        {
            return entry;
        }

        var clamped = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Take(OperationalEventDetail.MaxFailingTestNames)
            .ToArray();
        return entry with
        {
            Detail = entry.Detail! with { FailingTestNames = clamped }
        };
    }

    private void LoadRecent()
    {
        if (!File.Exists(journalPath))
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(journalPath);
            var loaded = new List<OperationalEvent>();
            var skippedMalformed = false;
            string? truncatedTail = null;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                OperationalEvent? record;
                try
                {
                    record = JsonSerializer.Deserialize<OperationalEvent>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    skippedMalformed = true;
                    if (i == lines.Length - 1)
                    {
                        truncatedTail = line;
                    }

                    // Middle-file corruption: skip the bad line; keep valid neighbours.
                    continue;
                }

                if (record is null
                    || record.SchemaVersion != OperationalHistorySchema.CurrentVersion
                    || string.IsNullOrWhiteSpace(record.Id)
                    || string.IsNullOrWhiteSpace(record.ProjectId))
                {
                    skippedMalformed = true;
                    if (i == lines.Length - 1)
                    {
                        truncatedTail = line;
                    }

                    continue;
                }

                if (knownIds.Add(record.Id))
                {
                    loaded.Add(record);
                }
            }

            if (!string.IsNullOrEmpty(truncatedTail))
            {
                TryWriteCorruptTail(truncatedTail);
            }

            entries.Clear();
            entries.AddRange(loaded.OrderByDescending(e => e.OccurredAtUtc).ThenBy(e => e.Id, StringComparer.Ordinal));
            knownIds.Clear();
            foreach (var e in entries)
            {
                knownIds.Add(e.Id);
            }

            var compacted = ApplyRetentionLocked(DateTimeOffset.UtcNow);
            if (compacted || skippedMalformed)
            {
                // Rewrite after load so a truncated tail / pruned age are not re-read forever.
                RewriteJournalLocked();
            }
        }
        catch (Exception ex)
        {
            entries.Clear();
            knownIds.Clear();
            onPersistenceWarning?.Invoke($"Operational history load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true when any entry was removed (caller should rewrite JSONL).
    /// </summary>
    private bool ApplyRetentionLocked(DateTimeOffset utcNow)
    {
        var cutoff = utcNow - maxAge;
        var before = entries.Count;
        entries.RemoveAll(e => e.OccurredAtUtc < cutoff);

        // Per-project count: keep newest N for each project.
        var overCap = false;
        foreach (var group in entries.GroupBy(e => e.ProjectId, StringComparer.OrdinalIgnoreCase).ToList())
        {
            var ordered = group
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList();
            if (ordered.Count <= maxEventsPerProject)
            {
                continue;
            }

            overCap = true;
            var dropIds = ordered.Skip(maxEventsPerProject).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            entries.RemoveAll(e => dropIds.Contains(e.Id));
        }

        knownIds.Clear();
        foreach (var e in entries)
        {
            knownIds.Add(e.Id);
        }

        // Keep newest-first order after removals.
        if (before != entries.Count || overCap)
        {
            entries.Sort(static (a, b) =>
            {
                var cmp = b.OccurredAtUtc.CompareTo(a.OccurredAtUtc);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
            });
        }

        return before != entries.Count || overCap;
    }

    private void AppendLineLocked(OperationalEvent entry)
    {
        try
        {
            File.AppendAllText(journalPath, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            onPersistenceWarning?.Invoke($"Operational history append failed: {ex.Message}");
        }
    }

    private void RewriteJournalLocked()
    {
        try
        {
            var lines = entries
                .OrderBy(e => e.OccurredAtUtc)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .Select(e => JsonSerializer.Serialize(e, JsonOptions));
            File.WriteAllLines(journalPath, lines);
        }
        catch (Exception ex)
        {
            onPersistenceWarning?.Invoke($"Operational history rewrite failed: {ex.Message}");
        }
    }

    private void TryWriteCorruptTail(string line)
    {
        try
        {
            File.WriteAllText(corruptTailPath, line);
            onPersistenceWarning?.Invoke(
                "Operational history: ignored truncated/malformed trailing JSONL record (quarantined).");
        }
        catch
        {
            // Best effort only.
        }
    }

    private static IReadOnlyList<OperationalEvent> TakeNewest(
        IEnumerable<OperationalEvent> source,
        int? limit)
    {
        if (limit is null)
        {
            return source.ToList();
        }

        if (limit <= 0)
        {
            return [];
        }

        return source.Take(limit.Value).ToList();
    }
}
