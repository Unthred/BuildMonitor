using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class AdaptiveFileChangeDebounce
{
    public const int MinDebounceMs = 1500;
    public const int MaxDebounceMs = 12000;
    public const int ColdStartSampleCount = 5;
    public const int DefaultDebounceMs = 3000;
    public const int MaxSamples = 50;

    public static int ResolveEffectiveDebounce(
        FileChangeDebounceMode mode,
        int manualDebounceMs,
        FileChangeBurstStats stats)
    {
        var manual = Math.Clamp(manualDebounceMs, MinDebounceMs, MaxDebounceMs);
        if (mode == FileChangeDebounceMode.Manual)
        {
            return manual;
        }

        if (stats.BurstSamplesMs.Count < ColdStartSampleCount)
        {
            return manual;
        }

        return Math.Clamp(stats.LearnedDebounceMs, MinDebounceMs, MaxDebounceMs);
    }

    public static int ComputeTargetDebounce(IReadOnlyList<int> burstSamplesMs)
    {
        if (burstSamplesMs.Count == 0)
        {
            return DefaultDebounceMs;
        }

        var p90 = Percentile(burstSamplesMs, 0.9);
        return Math.Clamp((int)Math.Round(p90 * 1.25), MinDebounceMs, MaxDebounceMs);
    }

    public static int ApplySessionPressure(int baseDebounceMs, int fileChangeBuildsInWindow)
    {
        var debounce = Math.Clamp(baseDebounceMs, MinDebounceMs, MaxDebounceMs);
        if (fileChangeBuildsInWindow < 2)
        {
            return debounce;
        }

        // Agent-style bursts: each extra recent file-triggered build adds 25% quiet time (cap 2×).
        var extraBuilds = Math.Min(fileChangeBuildsInWindow - 1, 4);
        var multiplier = 1.0 + 0.25 * extraBuilds;
        return Math.Clamp((int)Math.Round(debounce * multiplier), MinDebounceMs, MaxDebounceMs);
    }

    public static DateTimeOffset ComputeQuietUntilUtc(DateTimeOffset lastChangeUtc, int debounceMs) =>
        lastChangeUtc.AddMilliseconds(Math.Clamp(debounceMs, MinDebounceMs, MaxDebounceMs));

    public static int Smooth(int currentDebounceMs, int targetDebounceMs) =>
        (int)Math.Round(currentDebounceMs * 0.7 + targetDebounceMs * 0.3);

    public static FileChangeBurstStats RecordBurst(FileChangeBurstStats stats, int burstDurationMs)
    {
        if (burstDurationMs <= 0)
        {
            return stats;
        }

        var bursts = AppendSample(stats.BurstSamplesMs, burstDurationMs);
        var target = ComputeTargetDebounce(bursts);
        var learned = stats.BurstSamplesMs.Count < ColdStartSampleCount
            ? stats.LearnedDebounceMs
            : Smooth(stats.LearnedDebounceMs, target);

        return stats with
        {
            BurstSamplesMs = bursts,
            LearnedDebounceMs = learned
        };
    }

    public static FileChangeBurstStats RecordBuildDuration(FileChangeBurstStats stats, int buildDurationMs)
    {
        if (buildDurationMs <= 0)
        {
            return stats;
        }

        return stats with
        {
            BuildSamplesMs = AppendSample(stats.BuildSamplesMs, buildDurationMs)
        };
    }

    private static List<int> AppendSample(IReadOnlyList<int> samples, int value)
    {
        var updated = samples.Count == 0
            ? new List<int>(MaxSamples)
            : [.. samples];

        updated.Add(value);
        if (updated.Count > MaxSamples)
        {
            updated.RemoveRange(0, updated.Count - MaxSamples);
        }

        return updated;
    }

    private static double Percentile(IReadOnlyList<int> values, double percentile)
    {
        if (values.Count == 0)
        {
            return DefaultDebounceMs;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var rank = percentile * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var weight = rank - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
    }
}

public sealed record FileChangeBurstStats
{
    public List<int> BurstSamplesMs { get; init; } = [];
    public List<int> BuildSamplesMs { get; init; } = [];
    public int LearnedDebounceMs { get; init; } = AdaptiveFileChangeDebounce.DefaultDebounceMs;
}
