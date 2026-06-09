using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public class ProjectHealthEvaluatorTests
{
    [Fact]
    public void Evaluate_returns_red_when_build_failed()
    {
        var health = ProjectHealthEvaluator.Evaluate(
            ProjectLifecycleState.BuildFailed,
            lastBuildExitCode: 1,
            errorCount: 0,
            warningCount: 0);

        Assert.Equal(MonitorHealth.Red, health);
    }

    [Fact]
    public void Evaluate_returns_amber_when_watching_with_warnings()
    {
        var health = ProjectHealthEvaluator.Evaluate(
            ProjectLifecycleState.Watching,
            lastBuildExitCode: 0,
            errorCount: 0,
            warningCount: 3);

        Assert.Equal(MonitorHealth.Amber, health);
    }

    [Fact]
    public void Evaluate_returns_green_when_watching_clean()
    {
        var health = ProjectHealthEvaluator.Evaluate(
            ProjectLifecycleState.Watching,
            lastBuildExitCode: 0,
            errorCount: 0,
            warningCount: 0);

        Assert.Equal(MonitorHealth.Green, health);
    }
}
