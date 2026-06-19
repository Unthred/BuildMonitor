using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class BuildIntelligenceSnapshotTests
{
    [Theory]
    [InlineData(500, "500 ms")]
    [InlineData(1500, "1.5 s")]
    [InlineData(3000, "3 s")]
    public void FormatDuration_uses_seconds_above_one_second(int ms, string expected) =>
        Assert.Equal(expected, BuildIntelligenceSnapshot.FormatDuration(ms));

    [Fact]
    public void StatusHeadline_pending_rebuild_uses_countdown_text()
    {
        var snapshot = CreateSnapshot(
            pendingFileChangeRebuild: true,
            rebuildQuietUntilUtc: DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Contains("Rebuild in", snapshot.StatusHeadline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NextRebuildText_shows_countdown_when_queued()
    {
        var snapshot = CreateSnapshot(
            pendingFileChangeRebuild: true,
            rebuildQuietUntilUtc: DateTimeOffset.UtcNow.AddMilliseconds(2500));
        Assert.Contains("Rebuild in", snapshot.NextRebuildText, StringComparison.OrdinalIgnoreCase);
        Assert.True(snapshot.ShowRebuildCountdown);
    }

    [Fact]
    public void StrategyText_auto_learned_mentions_timing()
    {
        var stats = new FileChangeBurstStats { BurstSamplesMs = [2000, 3000, 4000, 5000, 6000], LearnedDebounceMs = 3200 };
        var snapshot = BuildIntelligenceSnapshot.Create(
            SampleProject(),
            new GlobalMonitorSettings { FileChangeDebounceMode = FileChangeDebounceMode.Auto },
            stats,
            manualDebounceMs: 3000,
            debounceMode: FileChangeDebounceMode.Auto,
            baseEffectiveDebounceMs: 3200,
            liveEffectiveDebounceMs: 3200,
            recentFileChangeBuildsIn90s: 0,
            coalesceWatchRebuilds: true,
            lastMeaningfulFileChangeUtc: null,
            pendingFileChangeRebuild: false,
            rebuildQuietUntilUtc: null);

        Assert.Contains("Auto timing", snapshot.StrategyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuietPeriodText_shows_elevated_wait_with_note()
    {
        var snapshot = CreateSnapshot(
            baseEffectiveDebounceMs: 3000,
            liveEffectiveDebounceMs: 4500,
            recentFileChangeBuildsIn90s: 3);

        Assert.Contains("now", snapshot.QuietPeriodText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3 file-triggered rebuilds", snapshot.QuietPeriodNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LearningText_manual_mode_mentions_settings()
    {
        var snapshot = CreateSnapshot(debounceMode: FileChangeDebounceMode.Manual);
        Assert.Contains("Settings", snapshot.LearningText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LearningText_auto_cold_start_shows_progress()
    {
        var stats = new FileChangeBurstStats { BurstSamplesMs = [1200, 800] };
        var snapshot = BuildIntelligenceSnapshot.Create(
            SampleProject(),
            new GlobalMonitorSettings { FileChangeDebounceMode = FileChangeDebounceMode.Auto },
            stats,
            manualDebounceMs: 3000,
            debounceMode: FileChangeDebounceMode.Auto,
            baseEffectiveDebounceMs: 3000,
            liveEffectiveDebounceMs: 3000,
            recentFileChangeBuildsIn90s: 0,
            coalesceWatchRebuilds: true,
            lastMeaningfulFileChangeUtc: null,
            pendingFileChangeRebuild: false,
            rebuildQuietUntilUtc: null);

        Assert.Contains("2/5", snapshot.LearningText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BurstBars_scale_relative_to_largest_sample()
    {
        var stats = new FileChangeBurstStats { BurstSamplesMs = [1000, 2000, 4000] };
        var snapshot = BuildIntelligenceSnapshot.Create(
            SampleProject(),
            new GlobalMonitorSettings(),
            stats,
            manualDebounceMs: 3000,
            debounceMode: FileChangeDebounceMode.Auto,
            baseEffectiveDebounceMs: 3000,
            liveEffectiveDebounceMs: 3000,
            recentFileChangeBuildsIn90s: 0,
            coalesceWatchRebuilds: true,
            lastMeaningfulFileChangeUtc: null,
            pendingFileChangeRebuild: false,
            rebuildQuietUntilUtc: null);

        Assert.Equal(3, snapshot.BurstBars.Count);
        Assert.Equal(1.0, snapshot.BurstBars.Last().HeightRatio, 3);
    }

    private static BuildIntelligenceSnapshot CreateSnapshot(
        FileChangeDebounceMode debounceMode = FileChangeDebounceMode.Auto,
        int baseEffectiveDebounceMs = 3000,
        int liveEffectiveDebounceMs = 3000,
        int recentFileChangeBuildsIn90s = 0,
        bool pendingFileChangeRebuild = false,
        DateTimeOffset? rebuildQuietUntilUtc = null) =>
        BuildIntelligenceSnapshot.Create(
            SampleProject(),
            new GlobalMonitorSettings { FileChangeDebounceMode = debounceMode },
            new FileChangeBurstStats { BurstSamplesMs = [2000], LearnedDebounceMs = 3200 },
            manualDebounceMs: 3000,
            debounceMode: debounceMode,
            baseEffectiveDebounceMs: baseEffectiveDebounceMs,
            liveEffectiveDebounceMs: liveEffectiveDebounceMs,
            recentFileChangeBuildsIn90s: recentFileChangeBuildsIn90s,
            coalesceWatchRebuilds: true,
            lastMeaningfulFileChangeUtc: null,
            pendingFileChangeRebuild: pendingFileChangeRebuild,
            rebuildQuietUntilUtc: rebuildQuietUntilUtc);

    private static LocalProjectDefinition SampleProject() => new()
    {
        Id = "p1",
        DisplayName = "Sample",
        IsActiveInSession = true,
        RootFolder = @"C:\sample",
        ProjectFile = "Sample.csproj"
    };
}
