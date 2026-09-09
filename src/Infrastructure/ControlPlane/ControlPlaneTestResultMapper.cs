using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.ControlPlane;

/// <summary>
/// Maps a parsed VSTest aggregate into control-plane test counts for /run/tests and ship-check.
/// Unknown aggregates are omitted — never fabricated as passed=0.
/// </summary>
public static class ControlPlaneTestResultMapper
{
    public const string CountsUnavailableMessage =
        "Test summary counts unavailable (could not parse VSTest aggregate).";

    public static ControlPlaneTestCounts? TryMapCounts(DotNetTestSummary? summary) =>
        summary is null
            ? null
            : new ControlPlaneTestCounts(summary.Failed, summary.Passed, summary.Skipped);

    /// <summary>
    /// Builds ok + counts for a completed test phase. When <paramref name="summary"/> is null,
    /// counts are omitted and a diagnostic is appended; lifecycle success is preserved via
    /// <paramref name="lifecycleTestOk"/>.
    /// </summary>
    public static (bool TestsOk, ControlPlaneTestCounts? Counts) MapCompletedTestPhase(
        DotNetTestSummary? summary,
        bool lifecycleTestOk,
        ICollection<string> failures)
    {
        var counts = TryMapCounts(summary);
        if (counts is null)
        {
            if (!failures.Contains(CountsUnavailableMessage))
            {
                failures.Add(CountsUnavailableMessage);
            }

            return (lifecycleTestOk, null);
        }

        var testsOk = lifecycleTestOk && counts.Failed == 0;
        return (testsOk, counts);
    }
}
