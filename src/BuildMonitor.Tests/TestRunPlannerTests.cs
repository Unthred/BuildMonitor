using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class TestRunPlannerTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    public void ShouldTryNoBuildFirst_when_last_build_succeeded(int lastBuildExitCode, bool expected) =>
        Assert.Equal(expected, TestRunPlanner.ShouldTryNoBuildFirst(lastBuildExitCode));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void RequiresFullBuildFromStart_matches_failed_build(int lastBuildExitCode, bool expected) =>
        Assert.Equal(expected, TestRunPlanner.RequiresFullBuildFromStart(lastBuildExitCode));

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(0, false, false)]
    [InlineData(1, true, true)]
    [InlineData(1, false, false)]
    public void ShouldStopAppBeforeInitialTestBuild_only_when_build_failed_and_app_running(
        int lastBuildExitCode,
        bool wasRunProcessActive,
        bool expected) =>
        Assert.Equal(
            expected,
            TestRunPlanner.ShouldStopAppBeforeInitialTestBuild(lastBuildExitCode, wasRunProcessActive));

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldStopAppForStaleTestFallback_follows_run_state(bool wasRunProcessActive, bool expected) =>
        Assert.Equal(expected, TestRunPlanner.ShouldStopAppForStaleTestFallback(wasRunProcessActive));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void ShouldReleaseLocksForTestBuild_when_setting_or_app_stopped(
        bool releaseLocksSetting,
        bool appWasStoppedForTests,
        bool expected) =>
        Assert.Equal(
            expected,
            TestRunPlanner.ShouldReleaseLocksForTestBuild(releaseLocksSetting, appWasStoppedForTests));

    [Fact]
    public void ShouldRetryWithFullBuild_true_for_missing_assembly_after_no_build()
    {
        const string log = """
            Test run for C:\src\App\bin\Debug\net10.0\App.Tests.dll (.NETCoreApp,Version=v10.0)
            The test source file "C:\src\App\bin\Debug\net10.0\App.Tests.dll" provided was not found.
            """;

        Assert.True(TestRunPlanner.ShouldRetryWithFullBuild(usedNoBuild: true, log));
        Assert.False(TestRunPlanner.ShouldRetryWithFullBuild(usedNoBuild: false, log));
    }

    [Fact]
    public void ShouldRetryWithFullBuild_false_when_tests_executed()
    {
        const string log = "Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 s";

        Assert.False(TestRunPlanner.ShouldRetryWithFullBuild(usedNoBuild: true, log));
    }
}
