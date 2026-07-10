using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
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
    IReadOnlyList<int> RecentBuildDurationSamplesMs,
    IReadOnlyList<bool> RecentBuildSucceededSamples,
    int TodayTriggerCount,
    DateTimeOffset? RebuildQuietUntilUtc,
    PendingRebuildHoldReason HoldReason = PendingRebuildHoldReason.None,
    int PendingRebuildFileCount = 0,
    IReadOnlyList<string>? PendingRebuildSamplePaths = null,
    int RebuildTimerResetCount = 0)
{
    /// <summary>Why the rebuild wait was deferred or the quiet timer restarted.</summary>
    public string NextRebuildReasonText => BuildNextRebuildReasonText();

    /// <summary>One-line summary of what the monitor is doing for file-change rebuilds.</summary>
    public string StatusHeadline => BuildStatusHeadline();

    /// <summary>What happens next — rebuild countdown or idle.</summary>
    public string NextRebuildText => BuildNextRebuildText();

    /// <summary>Primary line in the Next rebuild tile.</summary>
    public string NextRebuildHeadline => BuildNextRebuildHeadline();

    /// <summary>Secondary line in the Next rebuild tile.</summary>
    public string NextRebuildSubtext => BuildNextRebuildSubtext();

    public bool ShowNextRebuildIdleState => IsActiveInSession && !PendingFileChangeRebuild;

    public bool ShowNextRebuildStrategy => PendingFileChangeRebuild
        || AgentSessionBackoff
        || IsLearningIncomplete
        || !IsAutoMode;

    /// <summary>0–100 progress through the quiet period when a rebuild is queued.</summary>
    public double RebuildCountdownPercent => BuildRebuildCountdownPercent();

    public bool ShowRebuildCountdown => PendingFileChangeRebuild && RebuildQuietUntilUtc is not null;

    /// <summary>File-triggered rebuild frequency — explains slowdowns.</summary>
    public string RecentRebuildsText => BuildRecentRebuildsText();

    /// <summary>How timing is chosen — folds mode, learning, and slowdown into one line.</summary>
    public string StrategyText => BuildStrategyText();

    /// <summary>Caption under the burst chart.</summary>
    public string BurstChartCaption => BuildBurstChartCaption();

    public string LastFileChangeText => BuildLastFileChangeText();

    public string WatchModeText => BuildWatchModeText();

    public string QuietPeriodHelpText =>
        "Quiet period after edits stop before a file-triggered rebuild can start.";

    public string RecentRebuildsHelpText =>
        "File-triggered rebuilds in the last 90 seconds; busy sessions lengthen the wait.";

    public string TriggersTodayHelpText =>
        "All build triggers logged today for this project (see table below).";

    public string TypicalBuildHelpText =>
        "Average compile time from recent file-triggered builds.";

    public string BurstChartHelpText =>
        "Length of multi-file save bursts; feeds auto debounce learning.";

    public string BuildDurationHelpText =>
        "How long recent file-triggered builds took to compile.";

    public string BuildOutcomeHelpText =>
        "Success (green) vs failure (red) for those recent builds.";

    public string NextRebuildHelpText =>
        "Whether a rebuild is queued, and when the last meaningful source save happened.";

    public string TabTitle => PendingFileChangeRebuild
        ? $"{ProjectDisplayName} •"
        : ProjectDisplayName;

    public bool IsAutoMode => DebounceModeLabel == "Auto";

    public bool IsLearningIncomplete =>
        IsAutoMode && BurstSampleCount < AdaptiveFileChangeDebounce.ColdStartSampleCount;

    public bool HasBurstChartData => RecentBurstSamplesMs.Count > 0;

    public bool HasBuildDurationChartData => RecentBuildDurationSamplesMs.Count > 0;

    public bool HasBuildOutcomeChartData => RecentBuildSucceededSamples.Count > 0;

    public IReadOnlyList<BurstBarVisual> BurstBars => BuildBurstBars();

    public IReadOnlyList<BurstBarVisual> BuildDurationBars => BuildDurationBarVisuals();

    public IReadOnlyList<BuildOutcomeBarVisual> BuildOutcomeBars => BuildOutcomeBarVisuals();

    public string BuildOutcomeChartCaption => BuildBuildOutcomeChartCaption();

    public string BuildOutcomeSummaryLabel => BuildBuildOutcomeSummaryLabel();

    public string CountdownRemainingText => BuildCountdownRemainingText();

    public string TodayTriggersText => TodayTriggerCount switch
    {
        0 => "No triggers logged today.",
        1 => "1 trigger today.",
        _ => $"{TodayTriggerCount} triggers today."
    };

    public string AverageRecentBuildDurationText
    {
        get
        {
            if (RecentBuildDurationSamplesMs.Count == 0)
            {
                return "No build times yet.";
            }

            var avg = (int)Math.Round(RecentBuildDurationSamplesMs.Average());
            return $"Typical build ~{FormatDuration(avg)}";
        }
    }

    public string RecentBuildDurationLabel =>
        RecentBuildDurationSamplesMs.Count == 0
            ? "—"
            : FormatDuration((int)Math.Round(RecentBuildDurationSamplesMs.Average()));

    public string BuildDurationChartCaption => HasBuildDurationChartData
        ? $"Newest on the right · {AverageRecentBuildDurationText}"
        : "Build times appear after file-triggered rebuilds complete.";

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
            TakeRecentBuildDurationSamples(stats.BuildSamplesMs),
            TakeRecentBuildSucceededSamples(stats.BuildSucceededSamples),
            0,
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
        DateTimeOffset? rebuildQuietUntilUtc,
        int todayTriggerCount = 0,
        PendingRebuildHoldReason holdReason = PendingRebuildHoldReason.None,
        int pendingRebuildFileCount = 0,
        IReadOnlyList<string>? pendingRebuildSamplePaths = null,
        int rebuildTimerResetCount = 0)
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
            TakeRecentBuildDurationSamples(stats.BuildSamplesMs),
            TakeRecentBuildSucceededSamples(stats.BuildSucceededSamples),
            todayTriggerCount,
            rebuildQuietUntilUtc,
            holdReason,
            pendingRebuildFileCount,
            pendingRebuildSamplePaths ?? [],
            rebuildTimerResetCount);
    }

    private string BuildCountdownRemainingText()
    {
        if (!ShowRebuildCountdown || RebuildQuietUntilUtc is not { } quietUntil)
        {
            return string.Empty;
        }

        var remainingMs = (int)Math.Max(0, (quietUntil - DateTimeOffset.UtcNow).TotalMilliseconds);
        return remainingMs > 0 ? FormatDuration(remainingMs) : "now";
    }

    private string BuildNextRebuildText()
    {
        if (!IsActiveInSession)
        {
            return "Project not active this session.";
        }

        if (!PendingFileChangeRebuild)
        {
            return "Watching — no rebuild queued.";
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

    private string BuildNextRebuildHeadline()
    {
        if (!IsActiveInSession)
        {
            return "—";
        }

        if (!PendingFileChangeRebuild)
        {
            return "All quiet";
        }

        if (ShowRebuildCountdown)
        {
            var remaining = CountdownRemainingText;
            return string.IsNullOrWhiteSpace(remaining) ? "Soon" : remaining;
        }

        return "Queued";
    }

    private string BuildNextRebuildSubtext()
    {
        if (!IsActiveInSession)
        {
            return "Enable this project to watch for changes.";
        }

        if (!PendingFileChangeRebuild)
        {
            return "Watching for file changes";
        }

        if (!string.IsNullOrWhiteSpace(NextRebuildReasonText))
        {
            return NextRebuildReasonText;
        }

        if (ShowRebuildCountdown)
        {
            return "Waiting for edit quiet period";
        }

        return "Rebuild queued after edits settle";
    }

    private string BuildNextRebuildReasonText()
    {
        if (!PendingFileChangeRebuild || HoldReason == PendingRebuildHoldReason.None)
        {
            return string.Empty;
        }

        return EditGatingDetailFormatter.FormatHoldReason(
            HoldReason,
            PendingRebuildFileCount,
            PendingRebuildSamplePaths,
            RebuildTimerResetCount,
            LiveEffectiveDebounceMs,
            AgentSessionBackoff);
    }

    internal static string FormatPendingFileSample(IReadOnlyList<string>? paths, int totalCount) =>
        EditGatingDetailFormatter.FormatPendingFileSample(paths, totalCount);

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

    private string BuildLastFileChangeText() =>
        string.IsNullOrWhiteSpace(LastFileChangeLocal)
            ? "Last file change: none yet"
            : $"Last file change: {LastFileChangeLocal}";

    private string BuildWatchModeText() =>
        CoalesceWatchRebuilds
            ? "Watch: batch rebuild after quiet period"
            : "Watch: dotnet watch per change";

    private string BuildStatusChipText()
    {
        if (!IsActiveInSession)
        {
            return "Inactive";
        }

        if (PendingFileChangeRebuild)
        {
            var remaining = BuildCountdownRemainingText();
            return string.IsNullOrWhiteSpace(remaining) || remaining == "now"
                ? "Rebuild queued"
                : $"Rebuild queued · ~{remaining}";
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

    private IReadOnlyList<BurstBarVisual> BuildDurationBarVisuals()
    {
        if (RecentBuildDurationSamplesMs.Count == 0)
        {
            return [];
        }

        var max = Math.Max(
            RecentBuildDurationSamplesMs.Max(),
            AdaptiveFileChangeDebounce.MinDebounceMs);

        return RecentBuildDurationSamplesMs
            .Select(ms => new BurstBarVisual(FormatDuration(ms), Math.Max(0.12, ms / (double)max)))
            .ToList();
    }

    private IReadOnlyList<BuildOutcomeBarVisual> BuildOutcomeBarVisuals()
    {
        if (RecentBuildSucceededSamples.Count == 0)
        {
            return [];
        }

        var count = Math.Min(
            RecentBuildSucceededSamples.Count,
            RecentBuildDurationSamplesMs.Count > 0
                ? RecentBuildDurationSamplesMs.Count
                : RecentBuildSucceededSamples.Count);

        var outcomes = RecentBuildSucceededSamples.TakeLast(count).ToList();
        var durations = RecentBuildDurationSamplesMs.TakeLast(count).ToList();
        var maxDuration = durations.Count > 0 ? Math.Max(durations.Max(), 1000) : 1000;

        var bars = new List<BuildOutcomeBarVisual>(outcomes.Count);
        for (var i = 0; i < outcomes.Count; i++)
        {
            var durationMs = i < durations.Count ? durations[i] : 0;
            var height = durationMs > 0
                ? Math.Max(0.35, durationMs / (double)maxDuration)
                : 0.55;
            var label = durationMs > 0
                ? $"{(outcomes[i] ? "Succeeded" : "Failed")} · {FormatDuration(durationMs)}"
                : outcomes[i] ? "Succeeded" : "Failed";
            bars.Add(new BuildOutcomeBarVisual(label, height, outcomes[i]));
        }

        return bars;
    }

    private string BuildBuildOutcomeChartCaption()
    {
        if (!HasBuildOutcomeChartData)
        {
            return "Recent build outcomes appear after file-triggered rebuilds complete.";
        }

        return $"Newest on the right · {BuildOutcomeSummaryLabel}";
    }

    private string BuildBuildOutcomeSummaryLabel()
    {
        if (RecentBuildSucceededSamples.Count == 0)
        {
            return "No outcomes yet";
        }

        var succeeded = RecentBuildSucceededSamples.Count(static s => s);
        var failed = RecentBuildSucceededSamples.Count - succeeded;
        return failed == 0
            ? $"{succeeded} succeeded"
            : $"{succeeded} succeeded · {failed} failed";
    }

    private static IReadOnlyList<bool> TakeRecentBuildSucceededSamples(IReadOnlyList<bool> buildSucceededSamples) =>
        buildSucceededSamples.Count == 0
            ? []
            : buildSucceededSamples.TakeLast(5).ToList();

    private static IReadOnlyList<int> TakeRecentBurstSamples(IReadOnlyList<int> burstSamplesMs) =>
        burstSamplesMs.Count == 0
            ? []
            : burstSamplesMs.TakeLast(5).ToList();

    private static IReadOnlyList<int> TakeRecentBuildDurationSamples(IReadOnlyList<int> buildSamplesMs) =>
        buildSamplesMs.Count == 0
            ? []
            : buildSamplesMs.TakeLast(5).ToList();

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

public sealed record BuildOutcomeBarVisual(string Label, double HeightRatio, bool Succeeded)
{
    public double BarHeight => 10 + HeightRatio * 30;
}
