using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Diagnostics;

public sealed record BuildVerdictTrainingResult(
    IReadOnlyList<string> SuggestedExcludeSegments,
    bool AppliedDebounceFeedback);

public static class BuildVerdictTrainer
{
    public static bool IsFileChangeTrigger(BuildTriggerKind kind) =>
        kind is BuildTriggerKind.FileWatcher
            or BuildTriggerKind.FileWatcherQueued
            or BuildTriggerKind.DotNetWatchFileChange;

    public static BuildVerdictTrainingResult ProcessUnexpectedVerdict(
        BuildTriggerRecord record,
        string? configuredExcludeSegments,
        IReadOnlyList<string> learnedExcludeSegments,
        bool learnFromVerdicts,
        Action<string>? recordUnexpectedVerdict = null,
        Action<string>? recordDebounceFeedback = null)
    {
        if (!learnFromVerdicts)
        {
            return new BuildVerdictTrainingResult([], AppliedDebounceFeedback: false);
        }

        recordUnexpectedVerdict?.Invoke(record.ProjectId);

        var appliedDebounce = false;
        if (IsFileChangeTrigger(record.Kind))
        {
            recordDebounceFeedback?.Invoke(record.ProjectId);
            appliedDebounce = true;
        }

        var effective = WatchExcludeSegments.ResolveIgnoreSegmentSet(
            configuredExcludeSegments,
            learnedExcludeSegments);
        var suggestions = BuildTrainingExcludePlanner.SuggestExcludeSegments(
            record.ChangedPaths,
            effective);

        return new BuildVerdictTrainingResult(suggestions, appliedDebounce);
    }
}
