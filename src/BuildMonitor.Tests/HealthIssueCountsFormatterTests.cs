using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public class HealthIssueCountsFormatterTests
{
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
    public void FormatFailurePhase_maps_crashed_to_run_failed()
    {
        Assert.Equal("Run failed", HealthIssueCountsFormatter.FormatFailurePhase(ProjectLifecycleState.Crashed));
    }
}
