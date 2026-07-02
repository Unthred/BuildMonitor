using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class EditActivityEvaluatorTests
{
    [Fact]
    public void Evaluate_inactive_when_no_signals()
    {
        var snapshot = EditActivitySnapshot.Evaluate(
            new EditActivityInput(
                SourceWatcherHasPendingChanges: false,
                SourceBurstStartedUtc: null,
                LastMeaningfulFileChangeUtc: DateTimeOffset.MinValue,
                LastAgentActivityUtc: null,
                DebounceMs: 3000,
                UseAgentTranscriptActivity: true),
            DateTimeOffset.UtcNow);

        Assert.False(snapshot.IsActive);
    }

    [Fact]
    public void Evaluate_active_when_watcher_has_pending_changes()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = EditActivitySnapshot.Evaluate(
            new EditActivityInput(
                SourceWatcherHasPendingChanges: true,
                SourceBurstStartedUtc: now,
                LastMeaningfulFileChangeUtc: DateTimeOffset.MinValue,
                LastAgentActivityUtc: null,
                DebounceMs: 3000,
                UseAgentTranscriptActivity: false),
            now);

        Assert.True(snapshot.IsActive);
        Assert.True(snapshot.QuietUntilUtc > now);
    }

    [Fact]
    public void Evaluate_active_from_recent_agent_transcript_activity()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = EditActivitySnapshot.Evaluate(
            new EditActivityInput(
                SourceWatcherHasPendingChanges: false,
                SourceBurstStartedUtc: null,
                LastMeaningfulFileChangeUtc: DateTimeOffset.MinValue,
                LastAgentActivityUtc: now.AddMilliseconds(-500),
                DebounceMs: 3000,
                UseAgentTranscriptActivity: true),
            now);

        Assert.True(snapshot.IsActive);
    }
}
