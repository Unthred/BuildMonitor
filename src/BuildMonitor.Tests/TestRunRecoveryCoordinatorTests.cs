using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class TestRunRecoveryCoordinatorTests
{
    private const string MissingDebugDll = """
        Test run for C:\src\App\bin\Debug\net10.0\App.Tests.dll (.NETCoreApp,Version=v10.0)
        VSTest version 17.14.0 (x64)

        The test source file "C:\src\App\bin\Debug\net10.0\App.Tests.dll" provided was not found.
        """;

    private const string MissingFileAfterBanner = """
        Test run for C:\src\App\bin\Debug\net10.0\App.Tests.dll (.NETCoreApp,Version=v10.0)
        Could not find file 'C:\src\App\bin\Debug\net10.0\App.Tests.dll'.
        """;

    private const string AssertionFailure = """
        Test run for C:\src\App\bin\Debug\net10.0\App.Tests.dll (.NETCoreApp,Version=v10.0)
        Starting test execution, please wait...

          Failed App.Tests.MathTests.Adds [12 ms]
          Error Message:
           Assert.Equal() Failure
        Expected: 2
        Actual:   3

        Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 12 ms - App.Tests.dll (net10.0)
        """;

    private const string SuccessfulNoBuild = """
        Test run for C:\src\App\bin\Debug\net10.0\App.Tests.dll (.NETCoreApp,Version=v10.0)
        Starting test execution, please wait...

          Passed App.Tests.MathTests.Adds [5 ms]

        Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 5 ms - App.Tests.dll (net10.0)
        """;

    private const string RecoveryBuildFailed = """
        error CS0006: Metadata file 'App.dll' could not be found
        Build FAILED.
        """;

    [Fact]
    public async Task Missing_debug_dll_runs_recovery_build_then_retries_tests_once()
    {
        var runner = new RecordingRunner(
            new AttemptResult(1, MissingDebugDll),
            new AttemptResult(0, SuccessfulNoBuild));

        var result = await RunAsync(tryNoBuildFirst: true, runner);

        Assert.Equal(2, runner.Attempts.Count);
        Assert.True(runner.Attempts[0].NoBuild);
        Assert.False(runner.Attempts[0].IsRecoveryRetry);
        Assert.False(runner.Attempts[1].NoBuild);
        Assert.True(runner.Attempts[1].IsRecoveryRetry);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(SuccessfulNoBuild, result.Output);
    }

    [Fact]
    public async Task Missing_test_source_file_variant_uses_the_same_one_shot_recovery()
    {
        var runner = new RecordingRunner(
            new AttemptResult(1, MissingFileAfterBanner),
            new AttemptResult(0, SuccessfulNoBuild));

        var result = await RunAsync(tryNoBuildFirst: true, runner);

        Assert.Equal(2, runner.Attempts.Count);
        Assert.True(runner.Attempts[1].IsRecoveryRetry);
        Assert.False(runner.Attempts[1].NoBuild);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Genuine_assertion_failure_does_not_rebuild()
    {
        var runner = new RecordingRunner(new AttemptResult(1, AssertionFailure));

        var result = await RunAsync(tryNoBuildFirst: true, runner);

        Assert.Single(runner.Attempts);
        Assert.True(runner.Attempts[0].NoBuild);
        Assert.False(runner.Attempts[0].IsRecoveryRetry);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Assert.Equal()", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Successful_no_build_run_does_not_rebuild()
    {
        var runner = new RecordingRunner(new AttemptResult(0, SuccessfulNoBuild));

        var result = await RunAsync(tryNoBuildFirst: true, runner);

        Assert.Single(runner.Attempts);
        Assert.True(runner.Attempts[0].NoBuild);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Failed_recovery_is_surfaced_without_another_retry()
    {
        var runner = new RecordingRunner(
            new AttemptResult(1, MissingDebugDll),
            new AttemptResult(1, RecoveryBuildFailed));

        var result = await RunAsync(tryNoBuildFirst: true, runner);

        Assert.Equal(2, runner.Attempts.Count);
        Assert.True(runner.Attempts[1].IsRecoveryRetry);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Build FAILED.", result.Output, StringComparison.Ordinal);
        Assert.False(TestRunPlanner.ShouldRetryWithFullBuild(usedNoBuild: false, result.Output));
    }

    [Fact]
    public async Task Full_build_from_start_does_not_attempt_no_build_or_recovery_loop()
    {
        var runner = new RecordingRunner(new AttemptResult(1, MissingDebugDll));

        var result = await RunAsync(tryNoBuildFirst: false, runner);

        Assert.Single(runner.Attempts);
        Assert.False(runner.Attempts[0].NoBuild);
        Assert.False(runner.Attempts[0].IsRecoveryRetry);
        Assert.Equal(1, result.ExitCode);
    }

    private static Task<AttemptResult> RunAsync(bool tryNoBuildFirst, RecordingRunner runner) =>
        TestRunRecoveryCoordinator.RunWithOptionalFullBuildRecoveryAsync(
            tryNoBuildFirst,
            runner.RunAsync,
            static r => r.Output,
            CancellationToken.None);

    private sealed record AttemptResult(int ExitCode, string Output);

    private sealed class RecordingRunner
    {
        private readonly Queue<AttemptResult> results;

        public RecordingRunner(params AttemptResult[] results) =>
            this.results = new Queue<AttemptResult>(results);

        public List<TestRunAttempt> Attempts { get; } = [];

        public Task<AttemptResult> RunAsync(TestRunAttempt attempt, CancellationToken _)
        {
            Attempts.Add(attempt);
            if (results.Count == 0)
            {
                throw new InvalidOperationException("Unexpected extra test attempt (recovery loop).");
            }

            return Task.FromResult(results.Dequeue());
        }
    }
}
