using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class BuildIssueCountResolverTests
{
    private const string FullLog = """
        C:\app\Pages\Index.razor(1,1): warning CS8618: Field required [C:\app\app.csproj]

        Build succeeded.
            1066 Warning(s)
            0 Error(s)
        """;

    private const string IncrementalLog = """
        WitherbyConnect -> C:\app\bin\Debug\net9.0\WitherbyConnect.dll

        Build succeeded.
            0 Warning(s)
            0 Error(s)
        """;

    [Fact]
    public void Resolve_incremental_output_uses_previous_log_counts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "last-build.log");
        var prevPath = logPath + ".prev";

        try
        {
            File.WriteAllText(prevPath, FullLog);

            var (errors, warnings) = BuildIssueCountResolver.Resolve(IncrementalLog, logPath);

            Assert.Equal(0, errors);
            Assert.Equal(1066, warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ShouldApplyWatchOutputCounts_false_for_host_output_that_would_clear_counts()
    {
        const string runOutput = """
            info: Microsoft.Hosting.Lifetime[14]
                  Now listening on: http://localhost:5000
            info: Microsoft.Hosting.Lifetime[0]
                  Application started. Press Ctrl+C to shut down.
            """;

        Assert.False(BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
            runOutput,
            currentErrors: 0,
            currentWarnings: 1066,
            parsedErrors: 0,
            parsedWarnings: 0));
    }

    [Fact]
    public void ShouldApplyWatchOutputCounts_false_when_incremental_segment_would_clear_warnings()
    {
        Assert.False(BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
            IncrementalLog,
            currentErrors: 0,
            currentWarnings: 1066,
            parsedErrors: 0,
            parsedWarnings: 0));
    }

    [Fact]
    public void ShouldApplyWatchOutputCounts_false_when_build_succeeded_summary_would_clear_warnings()
    {
        const string segment = """
            Build succeeded.
                0 Warning(s)
                0 Error(s)
            """;

        Assert.False(BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
            segment,
            currentErrors: 0,
            currentWarnings: 1066,
            parsedErrors: 0,
            parsedWarnings: 0));
    }

    [Fact]
    public void ShouldApplyWatchOutputCounts_true_when_incremental_resolve_restores_stale_zero_counts()
    {
        Assert.True(BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
            IncrementalLog,
            currentErrors: 0,
            currentWarnings: 0,
            parsedErrors: 0,
            parsedWarnings: 1066));
    }

    [Fact]
    public void ShouldApplyWatchOutputCounts_false_when_parsed_matches_current()
    {
        Assert.False(BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
            IncrementalLog,
            currentErrors: 0,
            currentWarnings: 1066,
            parsedErrors: 0,
            parsedWarnings: 1066));
    }

    [Fact]
    public void Resolve_latest_watch_segment_uses_previous_log_when_incremental()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-watch-seg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "last-build.log");

        const string watchOutput = """
            info: Microsoft.Hosting.Lifetime[14]
                  Now listening on: http://localhost:5000
            WitherbyConnect -> C:\app\bin\Debug\net9.0\WitherbyConnect.dll

            Build succeeded.
                0 Warning(s)
                0 Error(s)
            """;

        try
        {
            File.WriteAllText(logPath + ".prev", FullLog);
            var segment = BuildLogParser.ExtractLatestBuildResultSegment(watchOutput);
            var (errors, warnings) = BuildIssueCountResolver.Resolve(segment, logPath);

            Assert.Equal(0, errors);
            Assert.Equal(1066, warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
