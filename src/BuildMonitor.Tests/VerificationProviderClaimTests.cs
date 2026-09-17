using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class VerificationProviderClaimTests
{
    [Fact]
    public void Exact_root_is_claimed_when_reachable_and_supported()
    {
        var worktree = @"C:\src\WitherbyConnectDotNet9";
        var decision = VerificationProviderClaim.Evaluate(
            worktree,
            [worktree + @"\"],
            providerReachable: true,
            supportsRequestedOperation: true);

        Assert.True(decision.Claimed);
        Assert.True(decision.ExclusiveOwnership);
        Assert.False(VerificationProviderSession.AllowsDirectDotNet(decision.Claimed));
        Assert.Equal(VerificationProviderDeclineReason.Claimed, decision.Reason);
    }

    [Fact]
    public void Parent_and_sibling_and_original_workspace_are_not_claims()
    {
        var configured = @"C:\src\WitherbyConnectDotNet9";
        var sibling = @"C:\src\WitherbyConnect-AB543";
        var originalWorkspace = @"C:\src\WitherbyConnect-AB449";
        var child = Path.Combine(configured, "docs");

        foreach (var worktree in new[] { sibling, originalWorkspace, child })
        {
            var decision = VerificationProviderClaim.Evaluate(
                worktree,
                [configured],
                providerReachable: true,
                supportsRequestedOperation: true);
            Assert.False(decision.Claimed, worktree);
            Assert.Equal(VerificationProviderDeclineReason.NoExactMatch, decision.Reason);
            Assert.True(VerificationProviderSession.AllowsDirectDotNet(decision.Claimed));
            Assert.Equal(VerificationProviderClaim.DirectFallbackName, decision.FallbackProviderName);
            Assert.Contains("Do not auto-configure", decision.Report, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Unreachable_provider_falls_back_even_on_exact_path()
    {
        var worktree = @"C:\src\BuildMonitor";
        var decision = VerificationProviderClaim.Evaluate(
            worktree,
            [worktree],
            providerReachable: false,
            supportsRequestedOperation: true);

        Assert.False(decision.Claimed);
        Assert.Equal(VerificationProviderDeclineReason.Unreachable, decision.Reason);
        Assert.True(VerificationProviderSession.AllowsDirectDotNet(decision.Claimed));
    }

    [Fact]
    public void Unsupported_operation_declines_exact_match()
    {
        var worktree = @"C:\src\BuildMonitor";
        var decision = VerificationProviderClaim.Evaluate(
            worktree,
            [worktree],
            providerReachable: true,
            supportsRequestedOperation: false);

        Assert.False(decision.Claimed);
        Assert.Equal(VerificationProviderDeclineReason.OperationUnsupported, decision.Reason);
    }

    [Fact]
    public void Final_verification_is_ship_check()
    {
        Assert.Equal(VerificationOperation.ShipCheck, VerificationProviderSession.FinalVerificationOperation);
        Assert.Equal("/run/ship-check", VerificationProviderSession.CommandName(VerificationOperation.ShipCheck));
    }
}
