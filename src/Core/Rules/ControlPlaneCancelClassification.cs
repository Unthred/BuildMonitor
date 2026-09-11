using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Failure-vs-cancel precedence for control-plane <c>/run/*</c> phases.
/// A late <c>CancelRequested</c> after normal process completion must not overwrite
/// an already-authoritative success/failure.
/// </summary>
public static class ControlPlaneCancelClassification
{
    /// <summary>
    /// True only when the lease cancel flag is set <em>and</em> the phase process
    /// actually terminated due to token cancellation.
    /// </summary>
    public static bool IsTokenOwnedAgentCancellation(bool cancelRequested, bool endedByTokenCancel) =>
        cancelRequested && endedByTokenCancel;

    /// <summary>
    /// Classify after a build phase that has already returned from <c>BuildAsync</c>.
    /// </summary>
    public static ControlPlaneOperationOutcome? TryClassifyCancelledBuild(
        bool cancelRequested,
        bool endedByTokenCancel) =>
        IsTokenOwnedAgentCancellation(cancelRequested, endedByTokenCancel)
            ? ControlPlaneOperationOutcome.Cancelled
            : null;

    /// <summary>
    /// Classify after a test phase that has already returned from <c>TestAsync</c>.
    /// </summary>
    public static ControlPlaneOperationOutcome? TryClassifyCancelledTests(
        bool cancelRequested,
        bool endedByTokenCancel) =>
        IsTokenOwnedAgentCancellation(cancelRequested, endedByTokenCancel)
            ? ControlPlaneOperationOutcome.Cancelled
            : null;

    /// <summary>
    /// Between ship-check build and tests: cancel may prevent the next phase without
    /// requiring token-owned termination of the prior phase.
    /// </summary>
    public static bool ShouldSkipNextPhaseDueToCancel(bool cancelRequested) =>
        cancelRequested;
}
