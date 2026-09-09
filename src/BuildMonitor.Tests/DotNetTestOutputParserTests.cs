using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class DotNetTestOutputParserTests
{
    [Fact]
    public void TryParseSummary_parses_vstest_passed_line()
    {
        const string log = """
            Starting test execution, please wait...
            Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 45 ms - BuildMonitor.Tests.dll (net10.0)
            """;

        var summary = DotNetTestOutputParser.TryParseSummary(log);

        Assert.NotNull(summary);
        Assert.Equal(12, summary!.Total);
        Assert.Equal(12, summary.Passed);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal("45 ms", summary.DurationText);
    }

    [Fact]
    public void TryParseSummary_parses_legacy_total_line()
    {
        const string log = "Total tests: 3. Passed: 2. Failed: 1. Skipped: 0. Total time: 1.2345 Seconds";

        var summary = DotNetTestOutputParser.TryParseSummary(log);

        Assert.NotNull(summary);
        Assert.Equal(3, summary!.Total);
        Assert.Equal(2, summary.Passed);
        Assert.Equal(1, summary.Failed);
    }

    [Fact]
    public void TryParseSummary_parses_multiline_success_omitted_zero_failed_skipped()
    {
        const string log = """
            Test Run Successful.
            Total tests: 1083
                 Passed: 1083
             Total time: 20.0077 Seconds
            """;

        var summary = DotNetTestOutputParser.TryParseSummary(log);

        Assert.NotNull(summary);
        Assert.Equal(1083, summary!.Total);
        Assert.Equal(1083, summary.Passed);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal("20.0077 Seconds", summary.DurationText);
    }

    [Fact]
    public void TryParseSummary_parses_multiline_with_failed_and_skipped()
    {
        const string log = """
            Test Run Failed.
            Total tests: 10
                 Passed: 7
                 Failed: 2
                 Skipped: 1
             Total time: 1.5 Seconds
            """;

        var summary = DotNetTestOutputParser.TryParseSummary(log);

        Assert.NotNull(summary);
        Assert.Equal(10, summary!.Total);
        Assert.Equal(7, summary.Passed);
        Assert.Equal(2, summary.Failed);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public void TryParseSummary_multiline_omitted_passed_derives_from_total()
    {
        const string log = """
            Test Run Failed.
            Total tests: 5
                 Failed: 2
                 Skipped: 1
             Total time: 0.1 Seconds
            """;

        var summary = DotNetTestOutputParser.TryParseSummary(log);

        Assert.NotNull(summary);
        Assert.Equal(5, summary!.Total);
        Assert.Equal(2, summary.Passed);
        Assert.Equal(2, summary.Failed);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public void TryParseSummary_rejects_inconsistent_multiline_aggregate()
    {
        const string log = """
            Test Run Successful.
            Total tests: 10
                 Passed: 9
                 Failed: 0
                 Skipped: 0
             Total time: 1 Seconds
            """;

        Assert.Null(DotNetTestOutputParser.TryParseSummary(log));
    }

    [Fact]
    public void TryParseSummary_last_valid_summary_wins()
    {
        const string log = """
            Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 1 ms - Old.dll (net10.0)
            Test Run Successful.
            Total tests: 1083
                 Passed: 1083
             Total time: 20.0077 Seconds
            """;

        var summary = DotNetTestOutputParser.TryParseSummary(log);

        Assert.NotNull(summary);
        Assert.Equal(1083, summary!.Total);
        Assert.Equal(1083, summary.Passed);
    }

    [Fact]
    public void TryParseSummary_last_valid_wins_when_earlier_multiline_is_stale()
    {
        const string log = """
            Test Run Successful.
            Total tests: 3
                 Passed: 3
             Total time: 1 Seconds
            more output
            Test Run Failed.
            Total tests: 10
                 Passed: 8
                 Failed: 2
             Total time: 2 Seconds
            """;

        var summary = DotNetTestOutputParser.TryParseSummary(log);

        Assert.NotNull(summary);
        Assert.Equal(10, summary!.Total);
        Assert.Equal(8, summary.Passed);
        Assert.Equal(2, summary.Failed);
    }

    [Fact]
    public void FormatSummaryLine_includes_counts_and_duration()
    {
        var summary = new DotNetTestSummary(12, 12, 0, 0, "45 ms", "BuildMonitor.Tests.dll (net10.0)");

        var line = DotNetTestOutputParser.FormatSummaryLine(summary);

        Assert.Contains("12 passed", line);
        Assert.Contains("45 ms", line);
    }

    [Fact]
    public void ParseIssues_extracts_vstest_failed_test_with_message()
    {
        const string log = """
            Starting test execution, please wait...
              Passed SampleTests.PassingTest [1 ms]
              Failed SampleTests.FailingTest [2 ms]
              Error Message:
               Assert.Equal() Failure
               Expected: 1
               Actual:   2
              Stack Trace:
                 at SampleTests.FailingTest() in C:\src\SampleTests.cs:line 10
            Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2, Duration: 5 ms
            """;

        var issues = DotNetTestOutputParser.ParseIssues(log);

        Assert.Contains(issues, i => i.IsError && i.Text.Contains("FailingTest"));
        Assert.Contains(issues, i => i.IsError && i.Text.Contains("Assert.Equal() Failure"));
        Assert.Equal(2, issues.First(i => i.IsError).LineNumber);
    }

    [Fact]
    public void ParseIssues_extracts_xunit_failed_test()
    {
        const string log = """
            [xUnit.net 00:00:00.12]     SampleTests.AnotherFailingTest [FAIL]
              Assert.True() Failure
              Expected: True
              Actual:   False
            """;

        var issues = DotNetTestOutputParser.ParseIssues(log);

        var failure = Assert.Single(issues, i => i.IsError);
        Assert.Contains("AnotherFailingTest", failure.Text);
        Assert.Contains("Assert.True() Failure", failure.Text);
    }

    [Fact]
    public void ParseIssues_includes_build_errors_when_tests_never_ran()
    {
        const string log = """
            C:\src\App.csproj : error NU1100: Unable to resolve package.
            """;

        var issues = DotNetTestOutputParser.ParseIssues(log);

        Assert.Single(issues);
        Assert.True(issues[0].IsError);
        Assert.Contains("error NU1100", issues[0].Text);
    }
}
