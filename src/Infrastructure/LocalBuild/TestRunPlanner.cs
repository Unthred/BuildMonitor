namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>Decides when to stop the app vs run tests with --no-build while the site stays up.</summary>
public static class TestRunPlanner
{
    public static bool ShouldTryNoBuildFirst(int lastBuildExitCode) => lastBuildExitCode == 0;

    /// <summary>Last build failed — a full test build is required before tests can run.</summary>
    public static bool RequiresFullBuildFromStart(int lastBuildExitCode) => lastBuildExitCode != 0;

    public static bool ShouldStopAppBeforeInitialTestBuild(int lastBuildExitCode, bool wasRunProcessActive) =>
        RequiresFullBuildFromStart(lastBuildExitCode) && wasRunProcessActive;

    public static bool ShouldStopAppForStaleTestFallback(bool wasRunProcessActive) => wasRunProcessActive;

    public static bool ShouldReleaseLocksForTestBuild(bool releaseLocksSetting, bool appWasStoppedForTests) =>
        releaseLocksSetting || appWasStoppedForTests;
}
