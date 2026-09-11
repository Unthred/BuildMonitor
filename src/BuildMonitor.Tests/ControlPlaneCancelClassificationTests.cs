using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneCancelClassificationTests
{
    [Fact]
    public void Late_cancel_after_build_failure_does_not_classify_cancelled()
    {
        // Build returned failure; CancelRequested flipped before classification.
        var classified = ControlPlaneCancelClassification.TryClassifyCancelledBuild(
            cancelRequested: true,
            endedByTokenCancel: false);
        Assert.Null(classified);
    }

    [Fact]
    public void Late_cancel_after_test_failure_does_not_classify_cancelled()
    {
        var classified = ControlPlaneCancelClassification.TryClassifyCancelledTests(
            cancelRequested: true,
            endedByTokenCancel: false);
        Assert.Null(classified);
    }

    [Fact]
    public void Token_owned_termination_with_lease_cancel_classifies_cancelled()
    {
        Assert.Equal(
            ControlPlaneOperationOutcome.Cancelled,
            ControlPlaneCancelClassification.TryClassifyCancelledBuild(
                cancelRequested: true,
                endedByTokenCancel: true));
        Assert.Equal(
            ControlPlaneOperationOutcome.Cancelled,
            ControlPlaneCancelClassification.TryClassifyCancelledTests(
                cancelRequested: true,
                endedByTokenCancel: true));
    }

    [Fact]
    public void Late_cancel_after_successful_phase_does_not_classify_cancelled()
    {
        Assert.Null(
            ControlPlaneCancelClassification.TryClassifyCancelledBuild(
                cancelRequested: true,
                endedByTokenCancel: false));
        Assert.Null(
            ControlPlaneCancelClassification.TryClassifyCancelledTests(
                cancelRequested: true,
                endedByTokenCancel: false));
    }

    [Fact]
    public void Between_ship_check_phases_cancel_may_skip_tests()
    {
        Assert.True(ControlPlaneCancelClassification.ShouldSkipNextPhaseDueToCancel(true));
        Assert.False(ControlPlaneCancelClassification.ShouldSkipNextPhaseDueToCancel(false));
    }

    [Fact]
    public void Ship_check_boundary_cancel_skips_tests_after_successful_build()
    {
        // Successful build completed without token-owned cancel…
        Assert.Null(
            ControlPlaneCancelClassification.TryClassifyCancelledBuild(
                cancelRequested: true,
                endedByTokenCancel: false));
        // …then cancel before tests may start.
        Assert.True(ControlPlaneCancelClassification.ShouldSkipNextPhaseDueToCancel(true));
    }

    [Fact]
    public void Completed_build_failure_is_classified_before_boundary_cancel_matters()
    {
        // Failure path: do not treat as cancelled; boundary skip is irrelevant once buildFailed is committed.
        Assert.Null(
            ControlPlaneCancelClassification.TryClassifyCancelledBuild(
                cancelRequested: true,
                endedByTokenCancel: false));
        Assert.Equal(
            ControlPlaneOperationOutcome.BuildFailed,
            ControlPlaneOperationOutcomeMapper.FromShipCheckBuildOnly(buildOk: false));
    }
}
