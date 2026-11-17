using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

public sealed class ProjectOrchestratorStopAllTests
{
    [Fact]
    public async Task Stops_run_concurrently_not_one_after_another()
    {
        const int projectCount = 7;
        using var allStarted = new CountdownEvent(projectCount);
        var reachedTogether = 0;

        // Each stop blocks (like a host's grace period) until every stop has begun.
        // Run one at a time, the first would time out waiting for the others.
        Func<Task> BlockingStop() => () =>
        {
            allStarted.Signal();
            if (allStarted.Wait(TimeSpan.FromSeconds(10)))
            {
                Interlocked.Increment(ref reachedTogether);
            }

            return Task.CompletedTask;
        };

        await ProjectOrchestrator.StopConcurrentlyAsync(
            Enumerable.Range(0, projectCount).Select(_ => BlockingStop()));

        Assert.Equal(projectCount, reachedTogether);
    }

    [Fact]
    public async Task Failure_in_one_stop_does_not_skip_the_others()
    {
        var stopped = 0;

        var stops = new Func<Task>[]
        {
            () => throw new InvalidOperationException("host stop failed"),
            () => { Interlocked.Increment(ref stopped); return Task.CompletedTask; },
            () => { Interlocked.Increment(ref stopped); return Task.CompletedTask; }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ProjectOrchestrator.StopConcurrentlyAsync(stops));
        Assert.Equal(2, stopped);
    }
}
