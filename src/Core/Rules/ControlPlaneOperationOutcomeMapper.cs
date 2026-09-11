using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Shared terminal outcome classification for control-plane <c>/run/*</c> results.
/// Consumes structured evidence only — never inspects <c>failures[]</c> prose.
/// Invariant: <c>ok == true</c> iff outcome is <see cref="ControlPlaneOperationOutcome.Succeeded"/>.
/// </summary>
public static class ControlPlaneOperationOutcomeMapper
{
    public static ControlPlaneOperationOutcome FromRebuild(bool buildOk) =>
        buildOk
            ? ControlPlaneOperationOutcome.Succeeded
            : ControlPlaneOperationOutcome.BuildFailed;

    /// <summary>
    /// Ship-check with zero configured/discovered test targets is contract success (build-only).
    /// </summary>
    public static ControlPlaneOperationOutcome FromShipCheckBuildOnly(bool buildOk) =>
        FromRebuild(buildOk);

    public static ControlPlaneOperationOutcome FromTests(ControlPlaneTestPhaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        // Structured zero-target evidence wins over lifecycle — stale TestOk must not mask noTests.
        if (evidence.NoTargetsConfigured)
        {
            return ControlPlaneOperationOutcome.NoTests;
        }

        if (evidence.LifecycleTestOk)
        {
            return ControlPlaneOperationOutcome.Succeeded;
        }

        if (evidence.Counts is { Failed: > 0 })
        {
            return ControlPlaneOperationOutcome.TestsFailed;
        }

        return ControlPlaneOperationOutcome.ExecutionFailed;
    }

    public static ControlPlaneOperationOutcome FromShipCheck(
        bool buildOk,
        bool noTestTargetsConfigured,
        ControlPlaneTestPhaseEvidence? testEvidence)
    {
        if (!buildOk)
        {
            return ControlPlaneOperationOutcome.BuildFailed;
        }

        if (noTestTargetsConfigured)
        {
            return ControlPlaneOperationOutcome.Succeeded;
        }

        ArgumentNullException.ThrowIfNull(testEvidence);
        return FromTests(testEvidence);
    }

    public static ControlPlaneOperationOutcome Cancelled() =>
        ControlPlaneOperationOutcome.Cancelled;

    public static bool IsOkConsistent(bool ok, ControlPlaneOperationOutcome outcome) =>
        ok == (outcome == ControlPlaneOperationOutcome.Succeeded);
}
