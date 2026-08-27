using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Tests;

public sealed class AppQuitLifecycleTests
{
    [Fact]
    public void TryClaim_first_caller_accepted_second_already_in_progress()
    {
        var flag = 0;
        Assert.Equal(AppQuitClaimResult.Accepted, AppQuitLifecycle.TryClaim(ref flag));
        Assert.Equal(1, flag);
        Assert.Equal(AppQuitClaimResult.AlreadyInProgress, AppQuitLifecycle.TryClaim(ref flag));
        Assert.Equal(1, flag);
    }

    [Fact]
    public void ArmFailsafeThenScheduleGraceful_arms_failsafe_before_graceful_callback()
    {
        var order = new List<string>();
        AppQuitLifecycle.ArmFailsafeThenScheduleGraceful(
            () => order.Add("failsafe"),
            () => order.Add("graceful"));

        Assert.Equal(["failsafe", "graceful"], order);
    }

    [Fact]
    public void ArmFailsafeThenScheduleGraceful_failsafe_stays_armed_when_graceful_throws()
    {
        var failsafeArmed = false;
        Assert.Throws<InvalidOperationException>(() =>
            AppQuitLifecycle.ArmFailsafeThenScheduleGraceful(
                () => failsafeArmed = true,
                () => throw new InvalidOperationException("UI thread affinity")));

        Assert.True(failsafeArmed);
    }

    [Fact]
    public void TryInvokeQuitCallback_null_or_throwing_returns_false_without_propagating()
    {
        Assert.False(AppQuitLifecycle.TryInvokeQuitCallback(null));
        Assert.False(AppQuitLifecycle.TryInvokeQuitCallback(
            () => throw new InvalidOperationException("The calling thread cannot access this object")));
    }

    [Fact]
    public void TryInvokeQuitCallback_success_returns_true()
    {
        var ran = false;
        Assert.True(AppQuitLifecycle.TryInvokeQuitCallback(() => ran = true));
        Assert.True(ran);
    }

    [Theory]
    [InlineData(202, false, AppQuitHttpDisposition.Accepted)]
    [InlineData(404, false, AppQuitHttpDisposition.Unavailable)]
    [InlineData(503, false, AppQuitHttpDisposition.Unavailable)]
    [InlineData(500, false, AppQuitHttpDisposition.ServerError)]
    [InlineData(null, true, AppQuitHttpDisposition.AlreadyDown)]
    [InlineData(null, false, AppQuitHttpDisposition.AlreadyDown)]
    [InlineData(400, false, AppQuitHttpDisposition.ServerError)]
    public void Classify_matches_deploy_script_contract(
        int? status,
        bool transportFailed,
        AppQuitHttpDisposition expected) =>
        Assert.Equal(expected, AppQuitHttpDispositionClassifier.Classify(status, transportFailed));
}
