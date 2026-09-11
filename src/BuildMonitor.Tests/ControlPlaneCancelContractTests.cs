using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneCancelContractTests
{
    [Fact]
    public void Cancelled_outcome_is_not_ok()
    {
        var outcome = ControlPlaneOperationOutcomeMapper.Cancelled();
        Assert.Equal(ControlPlaneOperationOutcome.Cancelled, outcome);
        Assert.False(ControlPlaneOperationOutcomeMapper.IsOkConsistent(ok: true, outcome));
        Assert.True(ControlPlaneOperationOutcomeMapper.IsOkConsistent(ok: false, outcome));
    }

    [Fact]
    public void Rebuild_result_cancelled_keeps_ok_false()
    {
        var result = new ControlPlaneRebuildResult(
            Ok: false,
            Project: "Demo.csproj",
            Build: "cancelled",
            ExitCode: -1,
            Failures: [],
            Log: null,
            Outcome: ControlPlaneOperationOutcome.Cancelled);
        Assert.False(result.Ok);
        Assert.Equal(ControlPlaneOperationOutcome.Cancelled, result.Outcome);
        Assert.True(ControlPlaneOperationOutcomeMapper.IsOkConsistent(result.Ok, result.Outcome));
    }

    [Fact]
    public void Activity_exposes_lease_operation_id_and_cancelling_phase()
    {
        var now = DateTimeOffset.UtcNow;
        var controlPlane = ProjectControlPlaneSnapshot.Unused with
        {
            AgentTestsInProgress = true,
            ActiveOperationId = "lease-abc",
            ActiveOperationKind = ControlPlaneOperationKind.Tests,
            OperationCancelRequested = true
        };
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Testing,
            0,
            null,
            null,
            0,
            0,
            now,
            null,
            true,
            [],
            ControlPlane: controlPlane);

        var set = ProjectActivityBuilder.Build(snapshot, now);
        Assert.NotNull(set.Primary);
        Assert.Equal(ActivityPhaseKind.Cancelling, set.Primary!.Phase);
        Assert.Equal("Cancelling tests…", set.Primary.StatusText);
        Assert.Equal("lease-abc", set.Primary.OperationId);
    }

    [Fact]
    public void Activity_includes_operation_id_while_rebuild_runs()
    {
        var now = DateTimeOffset.UtcNow;
        var controlPlane = ProjectControlPlaneSnapshot.Unused with
        {
            AgentRebuildInProgress = true,
            AgentRebuildPhase = ControlPlaneShipCheckPhase.Building,
            ActiveOperationId = "lease-rebuild",
            ActiveOperationKind = ControlPlaneOperationKind.Rebuild
        };
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Demo",
            MonitorHealth.Green,
            "Success",
            ProjectLifecycleState.Building,
            0,
            null,
            null,
            0,
            0,
            now,
            null,
            true,
            [],
            ControlPlane: controlPlane);

        var set = ProjectActivityBuilder.Build(snapshot, now);
        Assert.Equal("lease-rebuild", set.Primary!.OperationId);
        Assert.Equal(ActivityPhaseKind.AgentRebuild, set.Primary.Phase);
    }

    [Fact]
    public void Lease_owns_fresh_operation_id()
    {
        using var a = new ControlPlaneOperationLease(ControlPlaneOperationKind.Rebuild);
        using var b = new ControlPlaneOperationLease(ControlPlaneOperationKind.Rebuild);
        Assert.False(string.IsNullOrWhiteSpace(a.OperationId));
        Assert.NotEqual(a.OperationId, b.OperationId);
        Assert.False(a.CancelRequested);
        Assert.False(a.RequestCancel());
        Assert.True(a.CancelRequested);
        Assert.True(a.RequestCancel());
    }

    [Fact]
    public void Failure_outcomes_remain_distinct_from_cancelled()
    {
        Assert.Equal(
            ControlPlaneOperationOutcome.BuildFailed,
            ControlPlaneOperationOutcomeMapper.FromRebuild(false));
        Assert.NotEqual(
            ControlPlaneOperationOutcome.Cancelled,
            ControlPlaneOperationOutcomeMapper.FromRebuild(false));
    }
}
