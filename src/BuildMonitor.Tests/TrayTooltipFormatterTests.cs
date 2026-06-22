using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class TrayTooltipFormatterTests
{
    [Fact]
    public void Format_building_uses_project_name()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Alpha",
            MonitorHealth.Green,
            "OK",
            ProjectLifecycleState.Building,
            null,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            null,
            false,
            true,
            null,
            null,
            false);

        var text = TrayTooltipFormatter.Format(snapshot, MonitorHealth.Green, isBuilding: true);

        Assert.Equal("Building — Alpha", text);
    }

    [Fact]
    public void Format_failure_includes_error_preview_truncated()
    {
        var longError = new string('x', 80);
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Beta",
            MonitorHealth.Red,
            "Failed",
            ProjectLifecycleState.BuildFailed,
            1,
            TimeSpan.FromSeconds(3),
            longError,
            2,
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            true,
            [],
            null,
            false,
            true,
            "Build failed",
            "Build",
            false);

        var text = TrayTooltipFormatter.Format(snapshot, MonitorHealth.Red, isBuilding: false);

        Assert.StartsWith("Beta — Build: ", text);
        Assert.True(text.Length <= TrayTooltipFormatter.MaxTooltipLength);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void DescribeHealthTooltip_maps_rollup_colours()
    {
        Assert.Equal("Build monitor - Success", TrayTooltipFormatter.DescribeHealthTooltip(MonitorHealth.Green));
        Assert.Equal("Build monitor - Failed", TrayTooltipFormatter.DescribeHealthTooltip(MonitorHealth.Red));
    }
}
