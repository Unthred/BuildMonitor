namespace BuildMonitor.Infrastructure.LocalBuild;

public static class BuildMonitorLogBanner
{
    public static string Format(int buildNumber, string reason, DateTimeOffset? timestamp = null)
    {
        var ts = (timestamp ?? DateTimeOffset.Now).ToString("yyyy-MM-dd HH:mm:ss");
        return $"[BuildMonitor] ===== Build #{buildNumber} started {ts} — {reason} =====";
    }

    public static string FormatFinished(int buildNumber, int exitCode)
    {
        var status = exitCode == 0 ? "succeeded" : "failed";
        return $"[BuildMonitor] ===== Build #{buildNumber} finished — {status} (exit {exitCode}) =====";
    }

    public static string FormatIncrementalNote(int errorCount, int warningCount)
    {
        var parts = new List<string>();
        if (errorCount > 0)
        {
            parts.Add($"{errorCount} error(s)");
        }

        if (warningCount > 0)
        {
            parts.Add($"{warningCount} warning(s)");
        }

        var counts = parts.Count > 0 ? string.Join(", ", parts) : "prior issue counts";
        return $"[BuildMonitor] Incremental build — compiler skipped (outputs up-to-date). "
               + $"Tray health uses {counts} from the previous full build log.";
    }

    public static string FormatTest(int testNumber, string reason, DateTimeOffset? timestamp = null)
    {
        var ts = (timestamp ?? DateTimeOffset.Now).ToString("yyyy-MM-dd HH:mm:ss");
        return $"[BuildMonitor] ===== Test run #{testNumber} started {ts} — {reason} =====";
    }

    public static string FormatTestFinished(
        int testNumber,
        int exitCode,
        string? details = null,
        TimeSpan? wallDuration = null)
    {
        var status = exitCode == 0 ? "passed" : "failed";
        var parts = new List<string> { status, $"(exit {exitCode})" };

        if (!string.IsNullOrWhiteSpace(details))
        {
            parts.Add(details.Trim());
        }

        if (wallDuration is { } duration && duration > TimeSpan.Zero)
        {
            parts.Add($"wall {FormatDuration(duration)}");
        }

        return $"[BuildMonitor] ===== Test run #{testNumber} finished — {string.Join(", ", parts)} =====";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:0.#}s"
            : $"{duration.TotalMilliseconds:0}ms";

    public static string PrependToLog(string bannerLine, string logText) =>
        string.IsNullOrWhiteSpace(logText)
            ? bannerLine
            : bannerLine + Environment.NewLine + logText;
}
