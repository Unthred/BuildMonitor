using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Central policy for whether the supervised run/watch host may be started or resumed.
/// Desired state is separate from temporary operational pause.
/// </summary>
public static class RunHostLifecyclePolicy
{
    /// <summary>
    /// Any path that would call <c>StartRunProcess</c> (auto-resume, crash recovery,
    /// post-build ensure, test restart) must pass this gate.
    /// </summary>
    public static bool MayStartOrRestartHost(DesiredRunHostState desired) =>
        desired == DesiredRunHostState.Running;

    /// <summary>
    /// After ship-check / agent rebuild pause, resume only when desired state remains Running.
    /// </summary>
    public static bool ShouldResumeHostAfterOperation(
        DesiredRunHostState desired,
        ProjectRunMode runMode) =>
        desired == DesiredRunHostState.Running && runMode != ProjectRunMode.None;

    /// <summary>
    /// Crash recovery applies only when the host is still desired Running.
    /// </summary>
    public static bool MayApplyCrashRecovery(
        DesiredRunHostState desired,
        int exitCode,
        bool restartOnCrash,
        int restartCount,
        int maxRestartRetries) =>
        desired == DesiredRunHostState.Running
        && exitCode != 0
        && restartOnCrash
        && restartCount < maxRestartRetries;
}
