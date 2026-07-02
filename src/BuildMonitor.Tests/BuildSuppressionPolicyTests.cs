using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class BuildSuppressionPolicyTests
{
    private static readonly BuildSuppressionSettings Enabled = new(true, true);

    [Theory]
    [InlineData("startup", true)]
    [InlineData("file change", true)]
    [InlineData("file change (queued)", true)]
    [InlineData("manual rebuild", false)]
    [InlineData("rebuild & restart", false)]
    [InlineData("startup (lock retry)", false)]
    public void ShouldCancelInFlightBuild_respects_build_reason(string reason, bool expected) =>
        Assert.Equal(expected, BuildSuppressionPolicy.ShouldCancelInFlightBuild(Enabled, reason));

    [Fact]
    public void ShouldDeferStartupBuild_when_activity_active()
    {
        var activity = new EditActivitySnapshot(true, DateTimeOffset.UtcNow.AddSeconds(5), "pending");
        Assert.True(BuildSuppressionPolicy.ShouldDeferStartupBuild(Enabled, activity));
    }

    [Fact]
    public void IsEditGatingActive_for_startup_deferred_hold()
    {
        var activity = EditActivitySnapshot.Inactive;
        Assert.True(BuildSuppressionPolicy.IsEditGatingActive(
            Enabled,
            pendingFileChangeRebuild: true,
            activity,
            PendingRebuildHoldReason.StartupDeferred));
    }
}
