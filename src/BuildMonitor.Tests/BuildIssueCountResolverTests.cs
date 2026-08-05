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
    public void Resolve_uses_current_log_only_even_when_previous_has_warnings()
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
            Assert.Equal(0, warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_reads_msbuild_summary_from_full_log()
    {
        var (errors, warnings) = BuildIssueCountResolver.Resolve(FullLog);
        Assert.Equal(0, errors);
        Assert.Equal(1066, warnings);
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
    public void ShouldApplyWatchOutputCounts_true_when_build_succeeded_clears_warnings()
    {
        Assert.True(BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
            IncrementalLog,
            currentErrors: 0,
            currentWarnings: 1066,
            parsedErrors: 0,
            parsedWarnings: 0));
    }

    [Fact]
    public void ShouldApplyWatchOutputCounts_true_when_build_succeeded_summary_segment()
    {
        const string segment = """
            Build succeeded.
                0 Warning(s)
                0 Error(s)
            """;

        Assert.True(BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
            segment,
            currentErrors: 0,
            currentWarnings: 1066,
            parsedErrors: 0,
            parsedWarnings: 0));
    }

    [Fact]
    public void ShouldApplyWatchOutputCounts_true_when_parsed_has_warnings()
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
    public void Resolve_latest_watch_segment_uses_current_summary_only()
    {
        const string watchOutput = """
            info: Microsoft.Hosting.Lifetime[14]
                  Now listening on: http://localhost:5000
            WitherbyConnect -> C:\app\bin\Debug\net9.0\WitherbyConnect.dll

            Build succeeded.
                0 Warning(s)
                0 Error(s)
            """;

        var segment = BuildLogParser.ExtractLatestBuildResultSegment(watchOutput);
        var (errors, warnings) = BuildIssueCountResolver.Resolve(segment);

        Assert.Equal(0, errors);
        Assert.Equal(0, warnings);
    }
}
