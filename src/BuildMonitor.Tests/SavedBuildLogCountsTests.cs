using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

/// <summary>
/// Saved <c>last-build.log</c> files end with a "Build #N finished" banner. Counts must come
/// from the MSBuild summary above it, or a reused startup build shows 0 warnings.
/// </summary>
public sealed class SavedBuildLogCountsTests
{
    private static readonly string Warning =
        @"C:\src\App\App.csproj : warning NU1903: Package 'System.Security.Cryptography.Pkcs' 9.0.11 has a known high severity vulnerability";

    private static string SavedLog(int buildNumber, int warnings, int exitCode = 0) =>
        string.Join('\n',
        [
            BuildMonitorLogBanner.Format(buildNumber, "startup", new DateTimeOffset(2026, 11, 17, 9, 35, 55, TimeSpan.Zero)),
            "",
            "  Determining projects to restore...",
            .. Enumerable.Repeat(Warning, warnings),
            "",
            exitCode == 0 ? "Build succeeded." : "Build FAILED.",
            "",
            .. Enumerable.Repeat(Warning, warnings),
            $"    {warnings} Warning(s)",
            "    0 Error(s)",
            "",
            "Time Elapsed 00:00:25.23",
            "",
            BuildMonitorLogBanner.FormatFinished(buildNumber, exitCode)
        ]);

    [Fact]
    public void Warning_count_is_read_from_summary_above_finish_banner()
    {
        var log = SavedLog(1, warnings: 14);

        Assert.Equal(14, BuildLogParser.ParseWarningCount(log));
        Assert.Equal(0, BuildLogParser.ParseErrorCount(log));
        Assert.Equal((0, 14), BuildIssueCountResolver.Resolve(log));
    }

    [Fact]
    public void Latest_build_wins_when_log_holds_two_builds()
    {
        var log = SavedLog(1, warnings: 3) + "\n" + SavedLog(2, warnings: 14);

        Assert.Equal(14, BuildLogParser.ParseWarningCount(log));
    }

    [Fact]
    public void Segment_starts_at_last_started_banner()
    {
        var log = SavedLog(1, warnings: 3) + "\n" + SavedLog(2, warnings: 14);

        var segment = BuildLogParser.ExtractLatestBuildResultSegment(log);

        Assert.StartsWith("[BuildMonitor] ===== Build #2 started", segment, StringComparison.Ordinal);
        Assert.EndsWith(BuildMonitorLogBanner.FormatFinished(2, 0), segment, StringComparison.Ordinal);
    }
}
