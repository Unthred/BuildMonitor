using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class WatchMsBuildSegmentTests
{
    private const string PreviousSuccess = """
        WitherbyConnect -> C:\src\app\bin\Debug\net9.0\app.dll

        Build succeeded.
            0 Warning(s)
            0 Error(s)
        """;

    private const string Msb4018 =
        @"C:\Program Files\dotnet\sdk\9.0.316\Sdks\Microsoft.NET.Sdk.StaticWebAssets\targets\Microsoft.NET.Sdk.StaticWebAssets.targets(679,5): error MSB4018: The ""DefineStaticWebAssets"" task failed unexpectedly. [C:\src\WitherbyConnectDotNet9\WitherbyConnect.csproj]";

    [Fact]
    public void Latest_msbuild_failed_summary_is_used_not_previous_success_zero_errors()
    {
        var log = $"""
            {PreviousSuccess}

            {Msb4018}
            error MSB4018: System.Text.Json.JsonException: Expected depth to be zero at the end of the JSON payload.
            Build FAILED.
                0 Warning(s)
                1 Error(s)

            dotnet watch ⌚ The build failed. Fix the error then save the file to try building again.
            """;

        Assert.Equal(1, BuildLogParser.ParseErrorCount(log));
        Assert.Equal(0, BuildLogParser.ParseWarningCount(log));
    }

    [Fact]
    public void Watch_host_failed_line_without_msbuild_failed_still_keeps_current_msb4018()
    {
        var log = $"""
            {PreviousSuccess}

            {Msb4018}
            error MSB4018: System.Text.Json.JsonException: Expected depth to be zero at the end of the JSON payload.
            dotnet watch ⌚ The build failed. Fix the error then save the file to try building again.
            """;

        Assert.True(BuildLogParser.ParseErrorCount(log) >= 1);
    }

    [Fact]
    public void Later_successful_build_does_not_retain_previous_failure()
    {
        var log = $"""
            {Msb4018}
            Build FAILED.
                0 Warning(s)
                1 Error(s)
            dotnet watch ⌚ The build failed. Fix the error then save the file to try building again.

            {PreviousSuccess}
            """;

        Assert.Equal(0, BuildLogParser.ParseErrorCount(log));
        Assert.Equal(0, BuildLogParser.ParseWarningCount(log));
    }

    [Fact]
    public void Older_failed_build_is_not_resurrected_after_a_later_failure()
    {
        var log = """
            Pages\Old.cs(1,1): error CS1002: ; expected
            Build FAILED.
                0 Warning(s)
                1 Error(s)

            C:\sdk\StaticWebAssets.targets(679,5): error MSB4018: The "DefineStaticWebAssets" task failed unexpectedly.
            Build FAILED.
                0 Warning(s)
                1 Error(s)
            """;

        Assert.Equal(1, BuildLogParser.ParseErrorCount(log));
        var issues = BuildLogParser.ParseIssues(BuildLogParser.ExtractLatestBuildResultSegment(log));
        Assert.Contains(issues, i => i.IsError && i.Text.Contains("MSB4018", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(issues, i => i.Text.Contains("CS1002", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Watch_host_status_text_is_not_an_authoritative_msbuild_zero_summary()
    {
        const string hostLine = "dotnet watch ⌚ The build failed. Fix the error then save the file to try building again.";

        Assert.False(BuildIssueCountResolver.HasDefinitiveBuildOutcome(hostLine));
        Assert.False(BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
            hostLine,
            currentErrors: 1,
            currentWarnings: 0,
            parsedErrors: 0,
            parsedWarnings: 0));
        Assert.True(BuildIssueCountResolver.HasDefinitiveBuildOutcome("""
            Build FAILED.
                0 Warning(s)
                1 Error(s)
            """));
    }
}
