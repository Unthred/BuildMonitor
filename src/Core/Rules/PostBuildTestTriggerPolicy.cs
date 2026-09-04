using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Decides whether a successful build should run automatic post-build tests.
/// Never schedules builds; origin must already be known from the build path.
/// </summary>
public static class PostBuildTestTriggerPolicy
{
    /// <param name="runTests">Project RunTests preference.</param>
    /// <param name="triggeredByFileChange">
    /// True when this build was started by the permitted file-change auto-build path
    /// (<c>buildTriggeredByFileChange</c>).
    /// </param>
    /// <param name="skipAutoBuildTests">
    /// Effective <c>ShouldSkipAutoBuildTests()</c> / suppressAutoBuildTests gate.
    /// </param>
    public static bool ShouldRunTestsAfterSuccessfulBuild(
        TestRunTrigger runTests,
        bool triggeredByFileChange,
        bool skipAutoBuildTests)
    {
        if (skipAutoBuildTests || runTests == TestRunTrigger.Off)
        {
            return false;
        }

        return runTests switch
        {
            TestRunTrigger.OnBuildSuccess => true,
            TestRunTrigger.OnFileChange => triggeredByFileChange,
            _ => false
        };
    }
}
