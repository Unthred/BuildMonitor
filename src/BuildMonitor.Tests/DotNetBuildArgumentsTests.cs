using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class DotNetBuildArgumentsTests
{
    [Theory]
    [InlineData("manual rebuild", true)]
    [InlineData("rebuild & restart", true)]
    [InlineData("startup", true)]
    [InlineData("file change", false)]
    public void RequiresFullRebuild_matches_explicit_user_actions(string reason, bool expected) =>
        Assert.Equal(expected, DotNetBuildArguments.RequiresFullRebuild(reason));

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
