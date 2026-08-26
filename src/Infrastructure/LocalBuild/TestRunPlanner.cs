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

    /// <summary>
    /// After a <c>--no-build</c> attempt, retry once with a full test build when assemblies
    /// look missing or stale. False after the recovery attempt (<paramref name="usedNoBuild"/> is false)
    /// and when tests actually executed (assertion failures, failed cases, successful runs).
    /// </summary>
    public static bool ShouldRetryWithFullBuild(bool usedNoBuild, string logText) =>
        usedNoBuild && DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(logText);
}
