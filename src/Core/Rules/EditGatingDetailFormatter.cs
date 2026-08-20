using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Shared hold-reason text for status panel and build intelligence.</summary>
public static class EditGatingDetailFormatter
{
    public static string FormatHoldReason(
        PendingRebuildHoldReason holdReason,
        int pendingFileCount,
        IReadOnlyList<string>? samplePaths,
        int timerResetCount,
        int liveDebounceMs,
        bool agentSessionBackoff)
    {
        if (holdReason == PendingRebuildHoldReason.None)
        {
            return string.Empty;
        }

        var files = FormatPendingFileSample(samplePaths, pendingFileCount);

        return holdReason switch
        {
            PendingRebuildHoldReason.EditsStillArriving when timerResetCount > 1 =>
                $"Wait timer reset ({timerResetCount}×) — {pendingFileCount} file(s) just saved{files}. Quiet period restarted.",
            PendingRebuildHoldReason.EditsStillArriving =>
                $"Wait timer reset — {pendingFileCount} file(s) just saved{files}. Quiet period restarted.",
            PendingRebuildHoldReason.EditsSettling => agentSessionBackoff
                ? $"Agent session — waiting {FormatDuration(liveDebounceMs)} after the last save{files}."
                : $"Waiting {FormatDuration(liveDebounceMs)} after the last save{files}.",
            PendingRebuildHoldReason.BuildInProgress =>
                "Rebuild queued — waiting for the current build to finish.",
            PendingRebuildHoldReason.TestsInProgress =>
                "Rebuild queued — waiting for tests to finish.",
            PendingRebuildHoldReason.PostBuildCooldown =>
                $"Rebuild queued — post-build cooldown; {pendingFileCount} file(s) arrived{files}.",
            PendingRebuildHoldReason.StartupDeferred =>
                $"Startup build deferred — waiting {FormatDuration(liveDebounceMs)} for edits to settle{files}.",
            PendingRebuildHoldReason.SupersededByNewEdits =>
                $"Build cancelled — newer changes detected; rebuilding when edits settle{files}.",
            _ => string.Empty
        };
    }

    /// <summary>Concise CHANGES-row secondary for the status-panel grid.</summary>
    public static string FormatChangesSecondary(
        PendingRebuildHoldReason holdReason,
        int timerResetCount,
        int liveDebounceMs,
        DateTimeOffset? quietUntilUtc,
        DateTimeOffset utcNow)
    {
        if (quietUntilUtc is { } until && until > utcNow)
        {
            var remainingMs = (int)(until - utcNow).TotalMilliseconds;
            if (remainingMs > 0)
            {
                return remainingMs < 1000
                    ? $"{remainingMs} ms remaining"
                    : $"{remainingMs / 1000.0:0.#}s remaining";
            }
        }

        return holdReason switch
        {
            PendingRebuildHoldReason.EditsStillArriving =>
                timerResetCount > 1
                    ? $"Quiet period restarted ({timerResetCount}×)"
                    : "Quiet period restarted",
            PendingRebuildHoldReason.EditsSettling =>
                $"Waiting {FormatDuration(liveDebounceMs)}",
            PendingRebuildHoldReason.BuildInProgress => "Waiting for current build",
            PendingRebuildHoldReason.TestsInProgress => "Waiting for tests",
            PendingRebuildHoldReason.PostBuildCooldown => "Post-build cooldown",
            PendingRebuildHoldReason.StartupDeferred => "Waiting for edits to settle",
            PendingRebuildHoldReason.SupersededByNewEdits => "Newer changes — will rebuild",
            _ => string.Empty
        };
    }

    public static string FormatCountdownRemaining(DateTimeOffset? quietUntilUtc, DateTimeOffset utcNow)
    {
        if (quietUntilUtc is not { } quietUntil)
        {
            return string.Empty;
        }

        var remainingMs = (int)Math.Max(0, (quietUntil - utcNow).TotalMilliseconds);
        if (remainingMs <= 0)
        {
            return "Rebuild starting…";
        }

        var remainingSeconds = (remainingMs + 999) / 1000;
        return remainingSeconds == 1
            ? "Rebuild in 1 s"
            : $"Rebuild in {remainingSeconds} s";
    }

    public static string FormatPanelDismissCountdown(DateTimeOffset? dismissAtUtc, DateTimeOffset utcNow)
    {
        if (dismissAtUtc is not { } dismissAt)
        {
            return string.Empty;
        }

        var remainingMs = (int)Math.Max(0, (dismissAt - utcNow).TotalMilliseconds);
        if (remainingMs <= 0)
        {
            return "Closing…";
        }

        var remainingSeconds = (remainingMs + 999) / 1000;
        return remainingSeconds == 1
            ? "Closing in 1 s"
            : $"Closing in {remainingSeconds} s";
    }

    public static string FormatPendingFileSample(IReadOnlyList<string>? paths, int totalCount)
    {
        if (paths is not { Count: > 0 })
        {
            return string.Empty;
        }

        var shown = string.Join(", ", paths.Take(2));
        if (totalCount > paths.Count)
        {
            return $" ({shown} +{totalCount - paths.Count} more)";
        }

        return $" ({shown})";
    }

    private static string FormatDuration(int milliseconds)
    {
        if (milliseconds < 1000)
        {
            return $"{milliseconds} ms";
        }

        var seconds = milliseconds / 1000.0;
        return seconds < 60
            ? $"{seconds:0.#} s"
            : $"{(int)Math.Round(seconds / 60)} min";
    }
}
