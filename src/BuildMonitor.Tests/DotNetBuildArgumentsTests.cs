using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class DotNetBuildArgumentsTests
{
    [Theory]
    [InlineData("manual rebuild", true)]
    [InlineData("rebuild & restart", true)]
    [InlineData("startup", true)]
    [InlineData("file change", false)]
    [InlineData("file change (queued)", false)]
    public void RequiresFullRebuild_matches_explicit_user_actions(string reason, bool expected) =>
        Assert.Equal(expected, DotNetBuildArguments.RequiresFullRebuild(reason));

    [Theory]
    [InlineData("file change", true, true)]
    [InlineData("file change", false, false)]
    [InlineData("file change (queued)", true, true)]
    [InlineData("startup", false, true)]
    [InlineData("manual rebuild", false, true)]
    public void ShouldForceFullRebuild_honours_setting_and_rebuild_reasons(
        string reason,
        bool forceCompleteWarningCounts,
        bool expected) =>
        Assert.Equal(
            expected,
            DotNetBuildArguments.ShouldForceFullRebuild(reason, forceCompleteWarningCounts));

    [Fact]
    public void ApplyFullRebuildFlag_adds_no_incremental_once()
    {
        var args = new List<string> { "build", "App.csproj" };
        DotNetBuildArguments.ApplyFullRebuildFlag(args, forceFullRebuild: true);
        DotNetBuildArguments.ApplyFullRebuildFlag(args, forceFullRebuild: true);
        Assert.Equal(3, args.Count);
        Assert.Contains("--no-incremental", args);
    }
}
