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
}
