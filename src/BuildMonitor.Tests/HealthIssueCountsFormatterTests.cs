using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public class HealthIssueCountsFormatterTests
{
    [Fact]
    public void FormatStatusLine_omits_zero_build_counts_while_running()
    {
        var text = HealthIssueCountsFormatter.FormatStatusLine(
            ProjectLifecycleState.Running,
            buildErrors: 0,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 0);

        Assert.Null(text);
    }

    [Fact]
    public void FormatStatusLine_shows_build_warnings_while_running_with_clean_run()
    {
        var text = HealthIssueCountsFormatter.FormatStatusLine(
            ProjectLifecycleState.Running,
            buildErrors: 0,
            buildWarnings: 1067,
            runErrors: 0,
            runWarnings: 0);

        Assert.Equal("Build: 0 errors | 1067 warnings", text);
    }

    [Fact]
    public void FormatStatusLine_prefers_run_errors_when_crashed()
    {
        var text = HealthIssueCountsFormatter.FormatStatusLine(
            ProjectLifecycleState.Crashed,
            buildErrors: 0,
            buildWarnings: 1051,
            runErrors: 17,
            runWarnings: 0);

        Assert.Equal("Run: 17 errors | 0 warnings", text);
    }

    [Fact]
    public void SelectPrimaryCounts_uses_run_counts_when_crashed_with_run_errors()
    {
        var (errors, warnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            ProjectLifecycleState.Crashed,
            0,
            1051,
            17,
            2);

        Assert.Equal(17, errors);
        Assert.Equal(2, warnings);
    }

    [Fact]
    public void SelectPrimaryCounts_uses_build_warnings_when_running_with_clean_run_output()
    {
        var (errors, warnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            ProjectLifecycleState.Running,
            0,
            1067,
            0,
            0);

        Assert.Equal(0, errors);
        Assert.Equal(1067, warnings);
    }

    [Fact]
    public void FormatFailurePhase_maps_crashed_to_run_failed()
    {
        Assert.Equal("Run failed", HealthIssueCountsFormatter.FormatFailurePhase(ProjectLifecycleState.Crashed));
    }

    [Fact]
    public void FormatFailurePhase_building_keeps_building_despite_previous_failed_exit()
    {
        Assert.Equal(
            "Building",
            HealthIssueCountsFormatter.FormatFailurePhase(ProjectLifecycleState.Building, lastBuildExitCode: 1));
    }

    [Fact]
    public void FormatFailurePhase_watching_with_failed_build_is_build_failed()
    {
        Assert.Equal(
            "Build failed",
            HealthIssueCountsFormatter.FormatFailurePhase(ProjectLifecycleState.Watching, lastBuildExitCode: 1));
    }

    [Fact]
    public void FormatFailurePhase_watching_with_successful_build_stays_watching()
    {
        Assert.Equal(
            "Watching",
            HealthIssueCountsFormatter.FormatFailurePhase(ProjectLifecycleState.Watching, lastBuildExitCode: 0));
    }

    [Fact]
    public void SelectPrimaryCounts_uses_build_counts_when_watch_rebuild_failed()
    {
        var (errors, warnings) = HealthIssueCountsFormatter.SelectPrimaryCounts(
            ProjectLifecycleState.Watching,
            buildErrors: 1,
            buildWarnings: 0,
            runErrors: 0,
            runWarnings: 4,
            lastBuildExitCode: 1);

        Assert.Equal(1, errors);
        Assert.Equal(0, warnings);
    }
}
