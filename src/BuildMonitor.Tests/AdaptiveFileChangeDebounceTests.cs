using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class AdaptiveFileChangeDebounceTests
{
    [Fact]
    public void ResolveEffectiveDebounce_manual_uses_configured_value()
    {
        var stats = new FileChangeBurstStats { LearnedDebounceMs = 9000 };

        var resolved = AdaptiveFileChangeDebounce.ResolveEffectiveDebounce(
            FileChangeDebounceMode.Manual,
            2500,
            stats);

        Assert.Equal(2500, resolved);
    }

    [Fact]
    public void ResolveEffectiveDebounce_auto_uses_manual_until_cold_start_complete()
    {
        var stats = new FileChangeBurstStats
        {
            BurstSamplesMs = [1000, 1200, 900, 1100],
            LearnedDebounceMs = 9000
        };

        var resolved = AdaptiveFileChangeDebounce.ResolveEffectiveDebounce(
            FileChangeDebounceMode.Auto,
            3000,
            stats);

        Assert.Equal(3000, resolved);
    }

    [Fact]
    public void RecordBurst_smooths_toward_p90_target()
    {
        var stats = new FileChangeBurstStats();
        for (var i = 0; i < 5; i++)
        {
            stats = AdaptiveFileChangeDebounce.RecordBurst(stats, 4000);
        }

        Assert.True(stats.LearnedDebounceMs >= AdaptiveFileChangeDebounce.MinDebounceMs);
        Assert.True(stats.LearnedDebounceMs <= AdaptiveFileChangeDebounce.MaxDebounceMs);
        Assert.Equal(5, stats.BurstSamplesMs.Count);
    }

    [Fact]
    public void ComputeTargetDebounce_clamps_high_burst()
    {
        var target = AdaptiveFileChangeDebounce.ComputeTargetDebounce(
            Enumerable.Repeat(20000, 10).ToArray());

        Assert.Equal(AdaptiveFileChangeDebounce.MaxDebounceMs, target);
    }

    [Fact]
    public void ApplySessionPressure_increases_debounce_after_repeated_file_builds()
    {
        Assert.Equal(3000, AdaptiveFileChangeDebounce.ApplySessionPressure(3000, 1));
        Assert.Equal(3750, AdaptiveFileChangeDebounce.ApplySessionPressure(3000, 2));
        Assert.Equal(6000, AdaptiveFileChangeDebounce.ApplySessionPressure(3000, 5));
        Assert.Equal(AdaptiveFileChangeDebounce.MaxDebounceMs,
            AdaptiveFileChangeDebounce.ApplySessionPressure(9000, 6));
    }

    [Fact]
    public void ComputeQuietUntilUtc_adds_debounce_to_last_change()
    {
        var last = new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);
        var quiet = AdaptiveFileChangeDebounce.ComputeQuietUntilUtc(last, 5000);
        Assert.Equal(last.AddSeconds(5), quiet);
    }
}
