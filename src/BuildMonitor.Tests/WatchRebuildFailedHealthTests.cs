using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

/// <summary>
/// Regression: a surviving <c>dotnet watch</c> host must not make a failed current
/// build appear healthy. Mirrors <c>ProjectRuntime.RefreshHealth</c> / <c>BuildSnapshot</c>.
/// </summary>
public sealed class WatchRebuildFailedHealthTests
{
    private const string Msb4018Line =
        @"C:\Program Files\dotnet\sdk\9.0.316\Sdks\Microsoft.NET.Sdk.StaticWebAssets\targets\Microsoft.NET.Sdk.StaticWebAssets.targets(679,5): error MSB4018: The ""DefineStaticWebAssets"" task failed unexpectedly. [C:\src\WitherbyConnectDotNet9\WitherbyConnect.csproj]";

    private const string JsonExceptionLine =
        "error MSB4018: System.Text.Json.JsonException: Expected depth to be zero at the end of the JSON payload.";

    private const string WatchRebuildFailedOutput = """
        info: Microsoft.Hosting.Lifetime[14]
              Now listening on: http://localhost:5000
        info: Microsoft.Hosting.Lifetime[0]
              Application started. Press Ctrl+C to shut down.

        WitherbyConnect -> C:\src\WitherbyConnectDotNet9\bin\Debug\net9.0\WitherbyConnect.dll

        Build succeeded.
            0 Warning(s)
            0 Error(s)

        dotnet watch ⌚ File changed: C:\src\WitherbyConnectDotNet9\Pages\Index.razor
        dotnet watch ⌚ Building...
        C:\Program Files\dotnet\sdk\9.0.316\Sdks\Microsoft.NET.Sdk.StaticWebAssets\targets\Microsoft.NET.Sdk.StaticWebAssets.targets(679,5): error MSB4018: The "DefineStaticWebAssets" task failed unexpectedly. [C:\src\WitherbyConnectDotNet9\WitherbyConnect.csproj]
        error MSB4018: System.Text.Json.JsonException: Expected depth to be zero at the end of the JSON payload.
        error MSB4018:    at System.Text.Json.Utf8JsonReader.Read()
        Build FAILED.
            0 Warning(s)
            1 Error(s)

        dotnet watch ⌚ The build failed. Fix the error then save the file to try building again.
        """;

    [Fact]
    public void Parser_recognises_msb4018_static_web_assets_error()
    {
        var log = $"{Msb4018Line}{Environment.NewLine}{JsonExceptionLine}";

        Assert.True(BuildLogParser.ParseErrorCount(log) >= 1);
        Assert.Contains(
            BuildLogParser.ParseIssues(log),
            issue => issue.IsError && issue.Text.Contains("MSB4018", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parser_counts_errors_from_watch_output_when_host_repeats_the_build_failed()
    {
        Assert.True(BuildLogParser.ParseErrorCount(WatchRebuildFailedOutput) >= 1);
    }

    [Fact]
    public void Snapshot_is_red_when_watch_host_is_alive_and_current_rebuild_failed()
    {
        // Watch process still running => lifecycle stays Watching (not BuildFailed).
        // Runtime records lastBuildExitCode = 1. Count parse of the watch tail may
        // currently wipe buildErrorCount back to 0.
        var parsedErrors = BuildLogParser.ParseErrorCount(WatchRebuildFailedOutput);
        var appliedErrors = parsedErrors;
        if (BuildIssueCountResolver.ShouldApplyWatchOutputCounts(
                BuildLogParser.ExtractLatestBuildResultSegment(WatchRebuildFailedOutput),
                currentErrors: 1,
                currentWarnings: 0,
                parsedErrors: parsedErrors,
                parsedWarnings: 0))
        {
            appliedErrors = parsedErrors;
        }

        var snapshot = ComposeWatchSnapshot(
            lastBuildExitCode: 1,
            buildErrors: appliedErrors,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 0);

        Assert.Equal(MonitorHealth.Red, snapshot.Health);
        Assert.Equal("Failed", snapshot.HealthLabel);
        Assert.Equal(ProjectLifecycleState.Watching, snapshot.State);
        Assert.True(snapshot.ErrorCount >= 1);
        Assert.Contains("fail", snapshot.FailurePhase, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(snapshot.IssueCountsText);
        Assert.Contains("error", snapshot.IssueCountsText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Needs fix",
            StatusPanelIdleRailFormatter.FormatIdleLabel(snapshot.Health, webReady: true));
    }

    [Fact]
    public void Snapshot_is_red_when_watching_with_failed_exit_code_even_if_counts_were_wiped()
    {
        var snapshot = ComposeWatchSnapshot(
            lastBuildExitCode: 1,
            buildErrors: 0,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 0);

        Assert.Equal(MonitorHealth.Red, snapshot.Health);
        Assert.Equal("Failed", snapshot.HealthLabel);
        Assert.Equal(ProjectLifecycleState.Watching, snapshot.State);
        Assert.Contains("fail", snapshot.FailurePhase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectPrimaryCounts_does_not_hide_build_errors_behind_clean_run_counts_while_watching()
    {
        var (errors, warnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            ProjectLifecycleState.Watching,
            buildErrors: 2,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 0);

        Assert.Equal(2, errors);
        Assert.Equal(0, warnings);
    }

    [Fact]
    public void SelectPrimaryCounts_failed_build_dominates_run_warnings_while_watching()
    {
        var (errors, warnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            ProjectLifecycleState.Watching,
            buildErrors: 2,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 5,
            lastBuildExitCode: 1);

        Assert.Equal(2, errors);
        Assert.Equal(0, warnings);
    }

    [Fact]
    public void Snapshot_recovers_to_green_after_successful_watch_rebuild()
    {
        var failed = ComposeWatchSnapshot(
            lastBuildExitCode: 1,
            buildErrors: 1,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 0);
        var recovered = ComposeWatchSnapshot(
            lastBuildExitCode: 0,
            buildErrors: 0,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 0);

        Assert.Equal(MonitorHealth.Red, failed.Health);
        Assert.Equal(MonitorHealth.Green, recovered.Health);
        Assert.Equal("Success", recovered.HealthLabel);
        Assert.Equal(ProjectLifecycleState.Watching, recovered.State);
        Assert.Equal("Watching", recovered.FailurePhase);
        Assert.Equal(
            "Site up",
            StatusPanelIdleRailFormatter.FormatIdleLabel(recovered.Health, webReady: true));
    }

    [Fact]
    public void Snapshot_run_errors_still_red_when_watch_build_succeeded()
    {
        var snapshot = ComposeWatchSnapshot(
            lastBuildExitCode: 0,
            buildErrors: 0,
            buildWarnings: 0,
            runErrors: 3,
            runWarnings: 0);

        Assert.Equal(MonitorHealth.Red, snapshot.Health);
        Assert.Equal(3, snapshot.ErrorCount);
        Assert.Equal("Run: 3 errors | 0 warnings", snapshot.IssueCountsText);
    }

    [Fact]
    public void Snapshot_ordinary_successful_watching_stays_healthy()
    {
        var snapshot = ComposeWatchSnapshot(
            lastBuildExitCode: 0,
            buildErrors: 0,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 0);

        Assert.Equal(MonitorHealth.Green, snapshot.Health);
        Assert.Equal(ProjectLifecycleState.Watching, snapshot.State);
        Assert.Equal(0, snapshot.ErrorCount);
        Assert.Equal("Watching", snapshot.FailurePhase);
    }

    [Fact]
    public void Direct_build_failed_lifecycle_stays_red()
    {
        var health = ProjectHealthEvaluator.Evaluate(
            ProjectLifecycleState.BuildFailed,
            lastBuildExitCode: 1,
            errorCount: 2,
            warningCount: 0);

        Assert.Equal(MonitorHealth.Red, health);
        Assert.Equal("Build failed", HealthIssueCountsFormatter.FormatFailurePhase(
            ProjectLifecycleState.BuildFailed,
            lastBuildExitCode: 1));
        var (errors, _) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            ProjectLifecycleState.BuildFailed,
            2,
            0,
            0,
            0,
            lastBuildExitCode: 1);
        Assert.Equal(2, errors);
    }

    [Fact]
    public void Evaluate_returns_red_when_watching_with_failed_last_build_exit_code()
    {
        var health = ProjectHealthEvaluator.Evaluate(
            ProjectLifecycleState.Watching,
            lastBuildExitCode: 1,
            errorCount: 0,
            warningCount: 0);

        Assert.Equal(MonitorHealth.Red, health);
    }

    private static ProjectHealthSnapshot ComposeWatchSnapshot(
        int lastBuildExitCode,
        int buildErrors,
        int buildWarnings,
        int runErrors,
        int runWarnings)
    {
        const ProjectLifecycleState state = ProjectLifecycleState.Watching;
        var (displayErrors, displayWarnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            state,
            buildErrors,
            buildWarnings,
            runErrors,
            runWarnings,
            lastBuildExitCode);
        var health = ProjectHealthEvaluator.Evaluate(
            state,
            lastBuildExitCode,
            displayErrors,
            displayWarnings);
        return new ProjectHealthSnapshot(
            ProjectId: "p1",
            DisplayName: "WitherbyConnect",
            Health: health,
            HealthLabel: ProjectHealthEvaluator.ToLabel(health),
            State: state,
            LastExitCode: lastBuildExitCode,
            LastDuration: null,
            LastErrorPreview: "The build failed. Fix the error then save the file to try building again.",
            ErrorCount: displayErrors,
            WarningCount: displayWarnings,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: DateTimeOffset.UtcNow,
            IsActive: true,
            ProgressSteps: [],
            ListenUrl: "http://localhost:5000",
            ListenUrlReady: true,
            SupportsAppRestart: true,
            IssueCountsText: HealthIssueCountsFormatter.FormatStatusLine(
                state,
                buildErrors,
                buildWarnings,
                runErrors,
                runWarnings,
                lastBuildExitCode),
            FailurePhase: HealthIssueCountsFormatter.FormatFailurePhase(state, lastBuildExitCode),
            LastBuildExitCode: lastBuildExitCode);
    }
}
