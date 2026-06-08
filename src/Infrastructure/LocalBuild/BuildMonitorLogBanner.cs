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

    public static string PrependToLog(string bannerLine, string logText) =>
        string.IsNullOrWhiteSpace(logText)
            ? bannerLine
            : bannerLine + Environment.NewLine + logText;
}
