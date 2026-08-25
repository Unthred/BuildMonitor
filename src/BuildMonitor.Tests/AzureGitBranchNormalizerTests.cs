using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class AzureGitBranchNormalizerTests
{
    [Theory]
    [InlineData("refs/heads/main", "main")]
    [InlineData("refs/heads/feature/x", "feature/x")]
    [InlineData("main", "main")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void ToShortName_cases(string? input, string? expected) =>
        Assert.Equal(expected, AzureGitBranchNormalizer.ToShortName(input));

    [Fact]
    public void Local_git_head_status_distinguishes_detached_and_unavailable()
    {
        Assert.Equal(LocalGitHeadStatus.Detached, new LocalGitContext(LocalGitHeadStatus.Detached, null, []).HeadStatus);
        Assert.Equal(LocalGitHeadStatus.Unavailable, new LocalGitContext(LocalGitHeadStatus.Unavailable, null, [], "missing").HeadStatus);
        Assert.Equal("main", new LocalGitContext(LocalGitHeadStatus.Branch, "main", []).CurrentBranch);
    }
}
