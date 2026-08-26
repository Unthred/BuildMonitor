namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>One test invocation: either the first attempt or the single full-build recovery.</summary>
public readonly record struct TestRunAttempt(bool NoBuild, bool IsRecoveryRetry);

/// <summary>
/// Runs tests with at most one full-build recovery when <c>--no-build</c> failed because
/// assemblies are missing or stale. Does not retry genuine test failures.
/// </summary>
public static class TestRunRecoveryCoordinator
{
    public static async Task<TResult> RunWithOptionalFullBuildRecoveryAsync<TResult>(
        bool tryNoBuildFirst,
        Func<TestRunAttempt, CancellationToken, Task<TResult>> runAttempt,
        Func<TResult, string> getOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runAttempt);
        ArgumentNullException.ThrowIfNull(getOutput);

        var first = new TestRunAttempt(NoBuild: tryNoBuildFirst, IsRecoveryRetry: false);
        var result = await runAttempt(first, cancellationToken).ConfigureAwait(false);

        if (!TestRunPlanner.ShouldRetryWithFullBuild(usedNoBuild: tryNoBuildFirst, getOutput(result)))
        {
            return result;
        }

        var recovery = new TestRunAttempt(NoBuild: false, IsRecoveryRetry: true);
        return await runAttempt(recovery, cancellationToken).ConfigureAwait(false);
    }
}
