using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class DotNetTestLiveProgressTrackerTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Console_result_lines_increment_completed_without_inventing_total()
    {
        var tracker = new DotNetTestLiveProgressTracker();
        tracker.Reset(Started);

        Assert.True(tracker.OnOutputLine("  Passed Sample.A [1 ms]"));
        Assert.True(tracker.OnOutputLine("  Failed Sample.B [2 ms]"));
        Assert.True(tracker.OnOutputLine("  Skipped Sample.C [3 ms]"));
        Assert.False(tracker.OnOutputLine("Starting test execution, please wait..."));
        Assert.False(tracker.OnOutputLine("[xUnit.net 00:00:00.12]     Sample.B [FAIL]"));

        var snap = tracker.ToSnapshot();
        Assert.Equal(3, snap.Completed);
        Assert.Null(snap.Total);
        Assert.Equal(Started, snap.StartedAtUtc);
    }

    [Fact]
    public void Summary_line_sets_authoritative_total_and_aggregate_completed()
    {
        var tracker = new DotNetTestLiveProgressTracker();
        tracker.Reset(Started);
        Assert.True(tracker.OnOutputLine("  Passed Sample.A [1 ms]"));
        Assert.True(tracker.OnOutputLine(
            "Passed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 5 ms"));

        var snap = tracker.ToSnapshot();
        Assert.Equal(3, snap.Completed);
        Assert.Equal(3, snap.Total);
        Assert.True(snap.Completed <= snap.Total);

        // Further result lines must not double-count after summary.
        Assert.False(tracker.OnOutputLine("  Passed Sample.Late [1 ms]"));
        Assert.Equal(3, tracker.ToSnapshot().Completed);
    }

    [Fact]
    public void Summary_reconciles_prior_result_line_counts_without_adding()
    {
        var tracker = new DotNetTestLiveProgressTracker();
        tracker.Reset(Started);
        Assert.True(tracker.OnOutputLine("  Passed A [1 ms]"));
        Assert.True(tracker.OnOutputLine("  Passed B [1 ms]"));
        Assert.True(tracker.OnOutputLine("  Failed C [1 ms]"));
        Assert.True(tracker.OnOutputLine("  Skipped D [1 ms]"));
        Assert.Equal(4, tracker.ToSnapshot().Completed);

        Assert.True(tracker.OnOutputLine(
            "Failed!  - Failed:     1, Passed:     2, Skipped:     1, Total:     4, Duration: 9 ms"));

        var snap = tracker.ToSnapshot();
        // Authoritative aggregate replaces line tally (4), does not become 8.
        Assert.Equal(4, snap.Completed);
        Assert.Equal(4, snap.Total);
        Assert.True(snap.Completed <= snap.Total!.Value);
    }

    [Fact]
    public void Second_run_after_Reset_starts_clean_with_new_StartedAt()
    {
        var tracker = new DotNetTestLiveProgressTracker();
        tracker.Reset(Started);
        tracker.OnOutputLine("  Passed Sample.A [1 ms]");
        tracker.OnOutputLine(
            "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 1 ms");
        Assert.Equal(1, tracker.ToSnapshot().Completed);
        Assert.Equal(1, tracker.ToSnapshot().Total);

        var secondStart = Started.AddMinutes(10);
        tracker.Reset(secondStart);
        Assert.Equal(0, tracker.ToSnapshot().Completed);
        Assert.Null(tracker.ToSnapshot().Total);
        Assert.Equal(secondStart, tracker.ToSnapshot().StartedAtUtc);

        Assert.True(tracker.OnOutputLine("  Failed Sample.B [2 ms]"));
        var snap = tracker.ToSnapshot();
        Assert.Equal(1, snap.Completed);
        Assert.Null(snap.Total);
        Assert.Equal(secondStart, snap.StartedAtUtc);
    }

    [Fact]
    public void Reset_clears_counters_and_preserves_new_start()
    {
        var tracker = new DotNetTestLiveProgressTracker();
        tracker.Reset(Started);
        tracker.OnOutputLine("  Passed Sample.A [1 ms]");
        var later = Started.AddMinutes(5);
        tracker.Reset(later);

        var snap = tracker.ToSnapshot();
        Assert.Equal(0, snap.Completed);
        Assert.Null(snap.Total);
        Assert.Equal(later, snap.StartedAtUtc);
    }

    [Fact]
    public void TryParseConsoleResultLine_matches_vstest_detailed_rows()
    {
        Assert.True(DotNetTestOutputParser.TryParseConsoleResultLine(
            "  Passed BuildMonitor.Tests.Foo [57 ms]",
            out var passed));
        Assert.Equal(DotNetTestOutputParser.ConsoleTestResultKind.Passed, passed);

        Assert.True(DotNetTestOutputParser.TryParseConsoleResultLine(
            "  Failed BuildMonitor.Tests.Bar [12 ms]",
            out var failed));
        Assert.Equal(DotNetTestOutputParser.ConsoleTestResultKind.Failed, failed);

        Assert.False(DotNetTestOutputParser.TryParseConsoleResultLine(
            "[xUnit.net 00:00:00.12]     Sample.Bar [FAIL]",
            out _));
    }
}
