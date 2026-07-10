using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class BuildVerdictTrainerTests
{
    [Fact]
    public void ProcessUnexpectedVerdict_suggests_docs_and_records_feedback()
    {
        var debounceRecorded = false;
        var trainingRecorded = false;

        var result = BuildVerdictTrainer.ProcessUnexpectedVerdict(
            new BuildTriggerRecord(
                "t1",
                "p1",
                "Demo",
                DateTimeOffset.UtcNow,
                BuildTriggerKind.FileWatcher,
                "file change",
                ChangedPaths: ["notes/guide.md"]),
            configuredExcludeSegments: ".cursor",
            learnedExcludeSegments: [],
            learnFromVerdicts: true,
            _ => trainingRecorded = true,
            _ => debounceRecorded = true);

        Assert.True(trainingRecorded);
        Assert.True(debounceRecorded);
        Assert.Contains("notes", result.SuggestedExcludeSegments);
    }

    [Fact]
    public void ProcessUnexpectedVerdict_skips_learning_when_disabled()
    {
        var result = BuildVerdictTrainer.ProcessUnexpectedVerdict(
            new BuildTriggerRecord(
                "t1",
                "p1",
                "Demo",
                DateTimeOffset.UtcNow,
                BuildTriggerKind.FileWatcher,
                "file change",
                ChangedPaths: ["docs/guide.md"]),
            configuredExcludeSegments: null,
            learnedExcludeSegments: [],
            learnFromVerdicts: false);

        Assert.Empty(result.SuggestedExcludeSegments);
        Assert.False(result.AppliedDebounceFeedback);
    }

    [Fact]
    public void RecordUnexpectedVerdict_increases_learned_debounce()
    {
        var stats = new FileChangeBurstStats { LearnedDebounceMs = 3000 };

        var updated = AdaptiveFileChangeDebounce.RecordUnexpectedVerdict(stats);

        Assert.True(updated.LearnedDebounceMs > 3000);
        Assert.Equal(1, updated.UnexpectedVerdictCount);
    }
}
