namespace BuildMonitor.Infrastructure.LocalBuild;

public static class IncrementalBuildDetector
{
    /// <summary>
    /// True when MSBuild succeeded with a 0/0 summary and no compiler diagnostic lines —
    /// typical of an incremental build where outputs were already up-to-date.
    /// </summary>
    public static bool WasCompileSkipped(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText)
            || !logText.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (BuildLogParser.ParseErrorCount(logText) > 0
            || BuildLogParser.ParseWarningCount(logText) > 0)
        {
            return false;
        }

        return BuildLogParser.ParseIssues(logText, maxWarnings: 1).Count == 0;
    }
}
