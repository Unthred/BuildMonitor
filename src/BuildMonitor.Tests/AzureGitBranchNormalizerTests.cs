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
}
