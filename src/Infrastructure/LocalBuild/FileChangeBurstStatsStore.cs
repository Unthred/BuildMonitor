using System.Text.Json;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed class FileChangeBurstStatsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object sync = new();
    private readonly string storePath;
    private Dictionary<string, FileChangeBurstStats> statsByProjectId = new(StringComparer.OrdinalIgnoreCase);

    public FileChangeBurstStatsStore(string appDataDirectory)
    {
        storePath = Path.Combine(appDataDirectory, "debounce-stats.json");
        Load();
    }

    public FileChangeBurstStats GetOrDefault(string projectId)
    {
        lock (sync)
        {
            return statsByProjectId.TryGetValue(projectId, out var stats)
                ? stats
                : new FileChangeBurstStats();
        }
    }

    public FileChangeBurstStats RecordBurst(string projectId, int burstDurationMs)
    {
        lock (sync)
        {
            var current = GetOrDefault(projectId);
            var updated = AdaptiveFileChangeDebounce.RecordBurst(current, burstDurationMs);
            statsByProjectId[projectId] = updated;
            SaveLocked();
            return updated;
        }
    }

    public FileChangeBurstStats RecordBuildDuration(string projectId, int buildDurationMs, bool succeeded)
    {
        lock (sync)
        {
            var current = GetOrDefault(projectId);
            var updated = AdaptiveFileChangeDebounce.RecordBuildDuration(current, buildDurationMs, succeeded);
            statsByProjectId[projectId] = updated;
            SaveLocked();
            return updated;
        }
    }

    public FileChangeBurstStats RecordUnexpectedVerdict(string projectId)
    {
        lock (sync)
        {
            var current = GetOrDefault(projectId);
            var updated = AdaptiveFileChangeDebounce.RecordUnexpectedVerdict(current);
            statsByProjectId[projectId] = updated;
            SaveLocked();
            return updated;
        }
    }

    private void Load()
    {
        if (!File.Exists(storePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(storePath);
            statsByProjectId = JsonSerializer.Deserialize<Dictionary<string, FileChangeBurstStats>>(json, JsonOptions)
                ?? new Dictionary<string, FileChangeBurstStats>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            statsByProjectId = new Dictionary<string, FileChangeBurstStats>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveLocked()
    {
        var directory = Path.GetDirectoryName(storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(statsByProjectId, JsonOptions);
        File.WriteAllText(storePath, json);
    }
}
