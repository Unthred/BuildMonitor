using System.Text.Json;

namespace BuildMonitor.Infrastructure.Diagnostics;

public sealed class BuildTrainingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object sync = new();
    private readonly string storePath;
    private Dictionary<string, ProjectBuildTraining> byProjectId = new(StringComparer.OrdinalIgnoreCase);

    public BuildTrainingStore(string appDataDirectory)
    {
        storePath = Path.Combine(appDataDirectory, "build-training.json");
        Load();
    }

    public IReadOnlyList<string> GetLearnedExcludeSegments(string projectId)
    {
        lock (sync)
        {
            return byProjectId.TryGetValue(projectId, out var training)
                ? training.LearnedExcludeSegments.ToList()
                : [];
        }
    }

    public IReadOnlyList<string> AddLearnedExcludeSegments(string projectId, IEnumerable<string> segments)
    {
        lock (sync)
        {
            var current = byProjectId.TryGetValue(projectId, out var existing)
                ? existing
                : new ProjectBuildTraining();

            var merged = new HashSet<string>(current.LearnedExcludeSegments, StringComparer.OrdinalIgnoreCase);
            foreach (var segment in segments)
            {
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    merged.Add(segment.Trim());
                }
            }

            var updated = current with
            {
                LearnedExcludeSegments = merged.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
            };
            byProjectId[projectId] = updated;
            SaveLocked();
            return updated.LearnedExcludeSegments;
        }
    }

    public void RecordUnexpectedVerdict(string projectId)
    {
        lock (sync)
        {
            var current = byProjectId.TryGetValue(projectId, out var existing)
                ? existing
                : new ProjectBuildTraining();
            byProjectId[projectId] = current with
            {
                UnexpectedVerdictCount = current.UnexpectedVerdictCount + 1
            };
            SaveLocked();
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
            byProjectId = JsonSerializer.Deserialize<Dictionary<string, ProjectBuildTraining>>(json, JsonOptions)
                ?? new Dictionary<string, ProjectBuildTraining>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            byProjectId = new Dictionary<string, ProjectBuildTraining>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveLocked()
    {
        var directory = Path.GetDirectoryName(storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(byProjectId, JsonOptions);
        File.WriteAllText(storePath, json);
    }
}

public sealed record ProjectBuildTraining
{
    public List<string> LearnedExcludeSegments { get; init; } = [];
    public int UnexpectedVerdictCount { get; init; }
}
