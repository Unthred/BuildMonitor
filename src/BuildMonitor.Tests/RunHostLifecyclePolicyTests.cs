using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class RunHostLifecyclePolicyTests
{
    [Theory]
    [InlineData(DesiredRunHostState.Running, true)]
    [InlineData(DesiredRunHostState.Stopped, false)]
    public void MayStartOrRestartHost_requires_desired_Running(
        DesiredRunHostState desired,
        bool expected) =>
        Assert.Equal(expected, RunHostLifecyclePolicy.MayStartOrRestartHost(desired));

    [Theory]
    [InlineData(DesiredRunHostState.Running, ProjectRunMode.Run, true)]
    [InlineData(DesiredRunHostState.Running, ProjectRunMode.Watch, true)]
    [InlineData(DesiredRunHostState.Running, ProjectRunMode.None, false)]
    [InlineData(DesiredRunHostState.Stopped, ProjectRunMode.Run, false)]
    [InlineData(DesiredRunHostState.Stopped, ProjectRunMode.Watch, false)]
    public void ShouldResumeHostAfterOperation_requires_desired_Running_and_run_mode(
        DesiredRunHostState desired,
        ProjectRunMode runMode,
        bool expected) =>
        Assert.Equal(
            expected,
            RunHostLifecyclePolicy.ShouldResumeHostAfterOperation(desired, runMode));

    [Fact]
    public void MayApplyCrashRecovery_when_desired_Running_and_policy_allows() =>
        Assert.True(RunHostLifecyclePolicy.MayApplyCrashRecovery(
            DesiredRunHostState.Running,
            exitCode: 1,
            restartOnCrash: true,
            restartCount: 0,
            maxRestartRetries: 5));

    [Fact]
    public void MayApplyCrashRecovery_blocked_when_desired_Stopped() =>
        Assert.False(RunHostLifecyclePolicy.MayApplyCrashRecovery(
            DesiredRunHostState.Stopped,
            exitCode: 1,
            restartOnCrash: true,
            restartCount: 0,
            maxRestartRetries: 5));

    [Fact]
    public void MayApplyCrashRecovery_blocked_when_RestartOnCrash_false() =>
        Assert.False(RunHostLifecyclePolicy.MayApplyCrashRecovery(
            DesiredRunHostState.Running,
            exitCode: 1,
            restartOnCrash: false,
            restartCount: 0,
            maxRestartRetries: 5));

    [Fact]
    public void MayApplyCrashRecovery_blocked_when_retries_exhausted() =>
        Assert.False(RunHostLifecyclePolicy.MayApplyCrashRecovery(
            DesiredRunHostState.Running,
            exitCode: 1,
            restartOnCrash: true,
            restartCount: 5,
            maxRestartRetries: 5));

    [Fact]
    public void MayApplyCrashRecovery_blocked_on_zero_exit() =>
        Assert.False(RunHostLifecyclePolicy.MayApplyCrashRecovery(
            DesiredRunHostState.Running,
            exitCode: 0,
            restartOnCrash: true,
            restartCount: 0,
            maxRestartRetries: 5));
}
