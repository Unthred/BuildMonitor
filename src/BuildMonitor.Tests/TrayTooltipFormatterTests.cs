using BuildMonitor.Core.Models;

using BuildMonitor.Core.Rules;



namespace BuildMonitor.Tests;



public sealed class TrayTooltipFormatterTests

{

    [Fact]

    public void Format_building_merges_project_name_and_status()

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



        var text = TrayTooltipFormatter.FormatMultiline(snapshot, MonitorHealth.Green, isBuilding: true);



        Assert.Equal("Building — Alpha", text);

    }



    [Fact]

    public void FormatMultiline_merges_project_name_with_build_results()

    {

        var longError = new string('x', 80);

        var snapshot = new ProjectHealthSnapshot(

            "p1",

            "Vessel Compliance",

            MonitorHealth.Amber,

            "Warnings",

            ProjectLifecycleState.Watching,

            0,

            null,

            longError,

            0,

            1065,

            DateTimeOffset.UtcNow,

            null,

            true,

            [],

            null,

            false,

            true,

            "Build: 0 errors | 1065 warnings",

            "Watching",

            false);



        var text = TrayTooltipFormatter.FormatMultiline(snapshot, MonitorHealth.Amber, isBuilding: false);



        Assert.StartsWith("Vessel Compliance — Warnings", text, StringComparison.Ordinal);

        Assert.Contains("Build: 0 errors | 1065 warnings", text, StringComparison.Ordinal);

        Assert.Contains(longError, text, StringComparison.Ordinal);

    }



    [Fact]

    public void FormatShort_is_empty_for_suppressed_shell_tooltip()

    {

        var snapshot = new ProjectHealthSnapshot(

            "p1",

            "Beta",

            MonitorHealth.Red,

            "Failed",

            ProjectLifecycleState.BuildFailed,

            1,

            null,

            "error",

            1,

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



        Assert.Equal(string.Empty, TrayTooltipFormatter.FormatShort(snapshot, MonitorHealth.Red, isBuilding: false));

    }



    [Fact]

    public void DescribeHealthTooltip_maps_rollup_colours()

    {

        Assert.Equal("Build monitor - Success", TrayTooltipFormatter.DescribeHealthTooltip(MonitorHealth.Green));

        Assert.Equal("Build monitor - Failed", TrayTooltipFormatter.DescribeHealthTooltip(MonitorHealth.Red));

    }

}


