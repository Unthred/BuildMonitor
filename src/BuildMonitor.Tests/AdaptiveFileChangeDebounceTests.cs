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
}
