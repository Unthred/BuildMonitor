using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Diagnostics;

public sealed record BuildIntelligenceSnapshot(
    string ProjectId,
    string ProjectDisplayName,
    bool IsActiveInSession,
    string DebounceModeLabel,
    int ManualDebounceMs,
    int LearnedDebounceMs,
    int BaseEffectiveDebounceMs,
    int LiveEffectiveDebounceMs,
    int BurstSampleCount,
    int BuildDurationSampleCount,
    string RecentBurstSummary,
    int ComputedTargetDebounceMs,
    int RecentFileChangeBuildsIn90s,
    bool AgentSessionBackoff,
    bool CoalesceWatchRebuilds,
    string LearningStatus,
    string? LastFileChangeLocal,
    bool PendingFileChangeRebuild,
    IReadOnlyList<int> RecentBurstSamplesMs,
    DateTimeOffset? RebuildQuietUntilUtc)
{
    /// <summary>One-line summary of what the monitor is doing for file-change rebuilds.</summary>
    public string StatusHeadline => BuildStatusHeadline();

    /// <summary>What happens next — rebuild countdown or idle.</summary>
    public string NextRebuildText => BuildNextRebuildText();

    /// <summary>0–100 progress through the quiet period when a rebuild is queued.</summary>
    public double RebuildCountdownPercent => BuildRebuildCountdownPercent();

    public bool ShowRebuildCountdown => PendingFileChangeRebuild && RebuildQuietUntilUtc is not null;

    /// <summary>File-triggered rebuild frequency — explains slowdowns.</summary>
    public string RecentRebuildsText => BuildRecentRebuildsText();

    /// <summary>How timing is chosen — folds mode, learning, and slowdown into one line.</summary>
    public string StrategyText => BuildStrategyText();

    /// <summary>Caption under the burst chart.</summary>
    public string BurstChartCaption => BuildBurstChartCaption();

    /// <summary>Last file change and watch mode.</summary>
    public string ActivityText => BuildActivityText();

    public string TabTitle => PendingFileChangeRebuild
        ? $"{ProjectDisplayName} •"
        : ProjectDisplayName;

    public bool IsAutoMode => DebounceModeLabel == "Auto";

    public bool IsLearningIncomplete =>
        IsAutoMode && BurstSampleCount < AdaptiveFileChangeDebounce.ColdStartSampleCount;

    public bool HasBurstChartData => RecentBurstSamplesMs.Count > 0;

    public IReadOnlyList<BurstBarVisual> BurstBars => BuildBurstBars();

    public string StatusChipText => BuildStatusChipText();

    public bool ShowStatusChip => StatusChipText.Length > 0;

    // Kept for tests and diagnostics tooling.
    public string QuietPeriodText => BuildQuietPeriodText();
    public string? QuietPeriodNote => BuildQuietPeriodNote();
    public bool HasQuietPeriodNote => !string.IsNullOrWhiteSpace(QuietPeriodNote);
    public string LearningText => BuildLearningText();

    public static BuildIntelligenceSnapshot FromStoredStats(
        LocalProjectDefinition project,
        GlobalMonitorSettings monitor,
        FileChangeBurstStats stats)
    {
        var manual = Math.Clamp(
            monitor.FileChangeDebounceMs,
            AdaptiveFileChangeDebounce.MinDebounceMs,
            AdaptiveFileChangeDebounce.MaxDebounceMs);
        var baseEffective = AdaptiveFileChangeDebounce.ResolveEffectiveDebounce(
            monitor.FileChangeDebounceMode,
            manual,
            stats);

        return new BuildIntelligenceSnapshot(
            project.Id,
            project.DisplayName,
            project.IsActiveInSession,
            FormatDebounceMode(monitor.FileChangeDebounceMode),
            manual,
            stats.LearnedDebounceMs,
            baseEffective,
            baseEffective,
            stats.BurstSamplesMs.Count,
            stats.BuildSamplesMs.Count,
            FormatRecentBursts(stats.BurstSamplesMs),
            AdaptiveFileChangeDebounce.ComputeTargetDebounce(stats.BurstSamplesMs),
            0,
            false,
            monitor.CoalesceWatchRebuilds,
            FormatLearningStatus(monitor.FileChangeDebounceMode, stats),
            null,
            false,
            TakeRecentBurstSamples(stats.BurstSamplesMs),
            null);
    }

    internal static BuildIntelligenceSnapshot Create(
        LocalProjectDefinition project,
        GlobalMonitorSettings monitor,
        FileChangeBurstStats stats,
        int manualDebounceMs,
        FileChangeDebounceMode debounceMode,
        int baseEffectiveDebounceMs,
        int liveEffectiveDebounceMs,
        int recentFileChangeBuildsIn90s,
        bool coalesceWatchRebuilds,
        DateTimeOffset? lastMeaningfulFileChangeUtc,
        bool pendingFileChangeRebuild,
        DateTimeOffset? rebuildQuietUntilUtc)
    {
        var agentBackoff = recentFileChangeBuildsIn90s >= 1;
        return new BuildIntelligenceSnapshot(
            project.Id,
            project.DisplayName,
            project.IsActiveInSession,
            FormatDebounceMode(debounceMode),
            manualDebounceMs,
            stats.LearnedDebounceMs,
            baseEffectiveDebounceMs,
            liveEffectiveDebounceMs,
            stats.BurstSamplesMs.Count,
            stats.BuildSamplesMs.Count,
            FormatRecentBursts(stats.BurstSamplesMs),
            AdaptiveFileChangeDebounce.ComputeTargetDebounce(stats.BurstSamplesMs),
            recentFileChangeBuildsIn90s,
            agentBackoff,
            coalesceWatchRebuilds,
            FormatLearningStatus(debounceMode, stats),
            FormatLastFileChangeLocal(lastMeaningfulFileChangeUtc),
            pendingFileChangeRebuild,
            TakeRecentBurstSamples(stats.BurstSamplesMs),
            rebuildQuietUntilUtc);
    }

    private string BuildNextRebuildText()
    {
        if (!IsActiveInSession)
        {
            return "Project not active this session.";
        }

        if (!PendingFileChangeRebuild)
        {
            return "Not waiting — no rebuild queued.";
        }

        if (RebuildQuietUntilUtc is not { } quietUntil)
        {
            return "Rebuild queued after edits settle.";
        }

        var remainingMs = (int)Math.Max(0, (quietUntil - DateTimeOffset.UtcNow).TotalMilliseconds);
        return remainingMs > 0
            ? $"Rebuild in ~{FormatDuration(remainingMs)}"
            : "Rebuild starting…";
    }

    private double BuildRebuildCountdownPercent()
    {
        if (!ShowRebuildCountdown || LiveEffectiveDebounceMs <= 0)
        {
            return 0;
        }

        var remainingMs = Math.Max(0, (RebuildQuietUntilUtc!.Value - DateTimeOffset.UtcNow).TotalMilliseconds);
        var elapsed = LiveEffectiveDebounceMs - remainingMs;
        return Math.Clamp(elapsed * 100.0 / LiveEffectiveDebounceMs, 0, 100);
    }

    private string BuildRecentRebuildsText() =>
        RecentFileChangeBuildsIn90s switch
        {
            0 => "No file-triggered rebuilds in the last 90 seconds.",
            1 => "1 file-triggered rebuild in the last 90 seconds.",
            _ => $"{RecentFileChangeBuildsIn90s} file-triggered rebuilds in the last 90 seconds."
        };

    private string BuildStrategyText()
    {
        if (AgentSessionBackoff && LiveEffectiveDebounceMs > BaseEffectiveDebounceMs)
        {
            return $"Slowed to {FormatDuration(LiveEffectiveDebounceMs)} after a burst of rebuilds "
                   + $"(normally {FormatDuration(BaseEffectiveDebounceMs)}).";
        }

        if (!IsAutoMode)
        {
            return $"Manual timing — {FormatDuration(ManualDebounceMs)} after edits stop (Settings).";
        }

        if (IsLearningIncomplete)
        {
            return $"Auto timing — calibrating from your saves ({BurstSampleCount}/"
                   + $"{AdaptiveFileChangeDebounce.ColdStartSampleCount} bursts so far).";
        }

        return $"Auto timing — about {FormatDuration(LearnedDebounceMs)} after edits stop.";
    }

    private string BuildBurstChartCaption()
    {
        if (!HasBurstChartData)
        {
            return "Save several files in quick succession to populate this chart.";
        }

        return $"Newest on the right · typical wait target ~{FormatDuration(ComputedTargetDebounceMs)}";
    }

    private string BuildStatusHeadline()
    {
        if (!IsActiveInSession)
        {
            return "Inactive this session — saved data only.";
        }

        if (PendingFileChangeRebuild)
        {
            return NextRebuildText;
        }

        if (RecentFileChangeBuildsIn90s >= 3)
        {
            return "Busy edit session — rebuilds are being batched.";
        }

        return "Watching for file changes.";
    }

    private string BuildQuietPeriodText()
    {
        if (LiveEffectiveDebounceMs == BaseEffectiveDebounceMs)
        {
            return FormatDuration(LiveEffectiveDebounceMs);
        }

        return $"{FormatDuration(LiveEffectiveDebounceMs)} now (usually {FormatDuration(BaseEffectiveDebounceMs)})";
    }

    private string? BuildQuietPeriodNote()
    {
        if (!AgentSessionBackoff || LiveEffectiveDebounceMs <= BaseEffectiveDebounceMs)
        {
            return null;
        }

        return RecentFileChangeBuildsIn90s switch
        {
            1 => "Raised after a recent file-triggered rebuild.",
            _ => $"Raised after {RecentFileChangeBuildsIn90s} file-triggered rebuilds in 90 s."
        };
    }

    private string BuildLearningText()
    {
        if (!IsAutoMode)
        {
            return $"Fixed quiet period from Settings ({FormatDuration(ManualDebounceMs)}). "
                   + $"{BurstSampleCount} burst(s) recorded.";
        }

        if (IsLearningIncomplete)
        {
            return $"Learning from saves ({BurstSampleCount}/{AdaptiveFileChangeDebounce.ColdStartSampleCount}). "
                   + $"Using {FormatDuration(ManualDebounceMs)} until ready.";
        }

        return $"Learned {FormatDuration(LearnedDebounceMs)} from save patterns "
               + $"({BurstSampleCount} bursts, {BuildDurationSampleCount} build times).";
    }

    private string BuildActivityText()
    {
        var lastChange = string.IsNullOrWhiteSpace(LastFileChangeLocal)
            ? "No file changes yet"
            : $"Last change {LastFileChangeLocal}";

        var watchMode = CoalesceWatchRebuilds ? "Batch watch rebuilds" : "dotnet watch per change";
        return $"{lastChange} · {watchMode}";
    }

    private string BuildStatusChipText()
    {
        if (!IsActiveInSession)
        {
            return "Inactive";
        }

        if (PendingFileChangeRebuild)
        {
            return "Rebuild queued";
        }

        if (AgentSessionBackoff && LiveEffectiveDebounceMs > BaseEffectiveDebounceMs)
        {
            return "Slowed";
        }

        if (IsLearningIncomplete)
        {
            return "Learning";
        }

        return string.Empty;
    }

    private IReadOnlyList<BurstBarVisual> BuildBurstBars()
    {
        if (RecentBurstSamplesMs.Count == 0)
        {
            return [];
        }

        var max = Math.Max(
            RecentBurstSamplesMs.Max(),
            AdaptiveFileChangeDebounce.MinDebounceMs);

        return RecentBurstSamplesMs
            .Select(ms => new BurstBarVisual(FormatDuration(ms), Math.Max(0.12, ms / (double)max)))
            .ToList();
    }

    private static IReadOnlyList<int> TakeRecentBurstSamples(IReadOnlyList<int> burstSamplesMs) =>
        burstSamplesMs.Count == 0
            ? []
            : burstSamplesMs.TakeLast(5).ToList();

    private static string? FormatLastFileChangeLocal(DateTimeOffset? utc) =>
        utc is { } t && t != DateTimeOffset.MinValue
            ? t.ToLocalTime().ToString("t")
            : null;

    private static string FormatDebounceMode(FileChangeDebounceMode mode) =>
        mode == FileChangeDebounceMode.Auto ? "Auto" : "Manual";

    private static string FormatLearningStatus(FileChangeDebounceMode mode, FileChangeBurstStats stats)
    {
        if (mode != FileChangeDebounceMode.Auto)
        {
            return $"Manual quiet period ({stats.BurstSamplesMs.Count} burst sample(s) recorded)";
        }

        if (stats.BurstSamplesMs.Count < AdaptiveFileChangeDebounce.ColdStartSampleCount)
        {
            return $"Auto learning ({stats.BurstSamplesMs.Count}/{AdaptiveFileChangeDebounce.ColdStartSampleCount} bursts)";
        }

        return $"Auto using learned {stats.LearnedDebounceMs} ms";
    }

    private static string FormatRecentBursts(IReadOnlyList<int> burstSamplesMs)
    {
        if (burstSamplesMs.Count == 0)
        {
            return "—";
        }

        var recent = burstSamplesMs.TakeLast(5).Select(FormatDuration);
        return string.Join(", ", recent);
    }

    internal static string FormatDuration(int milliseconds) =>
        milliseconds >= 1000
            ? $"{milliseconds / 1000.0:0.#} s"
            : $"{milliseconds} ms";
}

public sealed record BurstBarVisual(string Label, double HeightRatio)
{
    public double BarHeight => 6 + HeightRatio * 34;
}
