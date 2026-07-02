using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

public static class BuildTriggerDetailFormatter
{
    public static string FormatImmediateDebounce(int debounceMs) =>
        $"Quiet period {debounceMs} ms (saved immediately)";

    public static string FormatCoalescedBuild(
        int debounceMs,
        PendingRebuildHoldReason holdReason,
        int timerResetCount)
    {
        var parts = new List<string> { $"Quiet period {debounceMs} ms" };

        if (holdReason != PendingRebuildHoldReason.None)
        {
            parts.Add(DescribeHoldReason(holdReason, timerResetCount));
        }

        return string.Join(" · ", parts);
    }

    private static string DescribeHoldReason(PendingRebuildHoldReason reason, int timerResetCount) =>
        reason switch
        {
            PendingRebuildHoldReason.EditsStillArriving when timerResetCount > 1 =>
                $"timer reset {timerResetCount}× while edits continued",
            PendingRebuildHoldReason.EditsStillArriving =>
                "timer reset while edits continued",
            PendingRebuildHoldReason.EditsSettling =>
                "waiting for edits to settle",
            PendingRebuildHoldReason.BuildInProgress =>
                "held until build finished",
            PendingRebuildHoldReason.TestsInProgress =>
                "held until tests finished",
            PendingRebuildHoldReason.PostBuildCooldown =>
                "held for post-build cooldown",
            PendingRebuildHoldReason.StartupDeferred =>
                "startup deferred until edits settle",
            PendingRebuildHoldReason.SupersededByNewEdits =>
                "build superseded by newer edits",
            _ => string.Empty
        };
}
