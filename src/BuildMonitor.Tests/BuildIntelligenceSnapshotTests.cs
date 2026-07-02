using BuildMonitor.Core.Models;
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

    [Fact]
    public void NextRebuildReasonText_timer_reset_mentions_files_and_restart()
    {
        var snapshot = CreateSnapshot(
            pendingFileChangeRebuild: true,
            rebuildQuietUntilUtc: DateTimeOffset.UtcNow.AddSeconds(3),
            holdReason: PendingRebuildHoldReason.EditsStillArriving,
            pendingRebuildFileCount: 3,
            pendingRebuildSamplePaths: ["Foo.cs", "Bar.cs"],
            rebuildTimerResetCount: 2);

        Assert.Contains("Wait timer reset", snapshot.NextRebuildReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3 file(s)", snapshot.NextRebuildReasonText, StringComparison.Ordinal);
        Assert.Contains("Quiet period restarted", snapshot.NextRebuildReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Foo.cs", snapshot.NextRebuildReasonText, StringComparison.Ordinal);
    }

    [Fact]
    public void NextRebuildReasonText_build_in_progress_explains_wait()
    {
        var snapshot = CreateSnapshot(
            pendingFileChangeRebuild: true,
            holdReason: PendingRebuildHoldReason.BuildInProgress);

        Assert.Contains("current build", snapshot.NextRebuildReasonText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NextRebuildReasonText_startup_deferred_mentions_quiet_period()
    {
        var snapshot = CreateSnapshot(
            pendingFileChangeRebuild: true,
            holdReason: PendingRebuildHoldReason.StartupDeferred);

        Assert.Contains("Startup build deferred", snapshot.NextRebuildReasonText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(new[] { "src/A.cs" }, 1, " (src/A.cs)")]
    [InlineData(new[] { "A.cs", "B.cs" }, 4, " (A.cs, B.cs +2 more)")]
    public void FormatPendingFileSample_formats_path_suffix(
        string[] paths,
        int totalCount,
        string expectedSuffix) =>
        Assert.EndsWith(
            expectedSuffix,
            BuildIntelligenceSnapshot.FormatPendingFileSample(paths, totalCount));

    private static BuildIntelligenceSnapshot CreateSnapshot(
        FileChangeDebounceMode debounceMode = FileChangeDebounceMode.Auto,
        int baseEffectiveDebounceMs = 3000,
        int liveEffectiveDebounceMs = 3000,
        int recentFileChangeBuildsIn90s = 0,
        bool pendingFileChangeRebuild = false,
        DateTimeOffset? rebuildQuietUntilUtc = null,
        PendingRebuildHoldReason holdReason = PendingRebuildHoldReason.None,
        int pendingRebuildFileCount = 0,
        IReadOnlyList<string>? pendingRebuildSamplePaths = null,
        int rebuildTimerResetCount = 0) =>
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
            rebuildQuietUntilUtc: rebuildQuietUntilUtc,
            holdReason: holdReason,
            pendingRebuildFileCount: pendingRebuildFileCount,
            pendingRebuildSamplePaths: pendingRebuildSamplePaths,
            rebuildTimerResetCount: rebuildTimerResetCount);

    private static LocalProjectDefinition SampleProject() => new()
    {
        Id = "p1",
        DisplayName = "Sample",
        IsActiveInSession = true,
        RootFolder = @"C:\sample",
        ProjectFile = "Sample.csproj"
    };
}
