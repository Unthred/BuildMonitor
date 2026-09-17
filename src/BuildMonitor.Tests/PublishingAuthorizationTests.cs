using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class PublishingAuthorizationTests
{
    [Theory]
    [InlineData(null, PublishingIntent.OrdinaryWork)]
    [InlineData("", PublishingIntent.OrdinaryWork)]
    [InlineData("Fix the tooltip", PublishingIntent.OrdinaryWork)]
    [InlineData("open PR", PublishingIntent.OpenPullRequest)]
    [InlineData("prepare a PR", PublishingIntent.OpenPullRequest)]
    [InlineData("ready for review", PublishingIntent.OpenPullRequest)]
    [InlineData("ship", PublishingIntent.OpenPullRequest)]
    [InlineData("ship it", PublishingIntent.OpenPullRequest)]
    [InlineData("merge", PublishingIntent.Merge)]
    [InlineData("please merge this pull request", PublishingIntent.Merge)]
    [InlineData("complete the ship", PublishingIntent.Merge)]
    public void Resolve_maps_current_instruction(string? instruction, PublishingIntent expected)
    {
        var intent = PublishingAuthorization.Resolve(instruction);
        Assert.Equal(expected, intent);
        Assert.Equal(expected == PublishingIntent.Merge, PublishingAuthorization.AllowsMerge(intent));
        Assert.Equal(
            expected is PublishingIntent.OpenPullRequest or PublishingIntent.Merge,
            PublishingAuthorization.AllowsOpenPullRequest(intent));
    }

    [Fact]
    public void Ship_alone_is_not_merge_authorization()
    {
        Assert.False(PublishingAuthorization.IsMergeAuthorized("ship"));
        Assert.False(PublishingAuthorization.IsMergeAuthorized("ship it"));
        Assert.False(PublishingAuthorization.AllowsMerge(PublishingAuthorization.Resolve("ship it")));
    }

    [Fact]
    public void Green_ci_is_not_human_merge_approval()
    {
        Assert.False(PublishingAuthorization.GreenCiIsHumanMergeApproval);
    }
}
