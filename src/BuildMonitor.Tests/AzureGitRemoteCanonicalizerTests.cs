using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class AzureGitRemoteCanonicalizerTests
{
    [Theory]
    [InlineData("https://dev.azure.com/contoso/Fiber/_git/Repo", "contoso", "Fiber", "Repo")]
    [InlineData("https://dev.azure.com/contoso/Fiber/_git/Repo.git", "contoso", "Fiber", "Repo")]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/Fiber/Repo", "contoso", "Fiber", "Repo")]
    [InlineData("https://contoso.visualstudio.com/Fiber/_git/Repo", "contoso", "Fiber", "Repo")]
    public void Parses_common_azure_remote_shapes(string url, string org, string project, string repo)
    {
        Assert.True(AzureGitRemoteCanonicalizer.TryParseAzureDevOpsRemote(url, out var id));
        Assert.Equal(org, id.Organization);
        Assert.Equal(project, id.Project);
        Assert.Equal(repo, id.Repository);
    }

    [Fact]
    public void Suggest_exact_url_match()
    {
        var remotes = new[] { new LocalGitRemote("origin", "https://dev.azure.com/org/P/_git/RepoA") };
        var projects = new[] { new AzureProjectSummary("p1", "P", null, null) };
        var repos = new[]
        {
            new AzureRepositorySummary("r1", "RepoA", "p1", "P", "https://dev.azure.com/org/P/_git/RepoA", null, "refs/heads/main", "main"),
            new AzureRepositorySummary("r2", "RepoB", "p1", "P", "https://dev.azure.com/org/P/_git/RepoB", null, null, null)
        };

        var suggestion = AzureRepositoryMatchSuggester.Suggest(remotes, projects, repos);
        Assert.NotNull(suggestion);
        Assert.Equal("r1", suggestion!.RepositoryId);
        Assert.Contains("URL", suggestion.MatchReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suggest_ambiguous_name_does_not_auto_match()
    {
        var remotes = new[] { new LocalGitRemote("origin", "https://dev.azure.com/org/P/_git/Shared") };
        var projects = new[] { new AzureProjectSummary("p1", "P", null, null) };
        var repos = new[]
        {
            new AzureRepositorySummary("r1", "Shared", "p1", "P", "https://dev.azure.com/org/P/_git/Other1", null, null, null),
            new AzureRepositorySummary("r2", "Shared", "p1", "P", "https://dev.azure.com/org/P/_git/Other2", null, null, null)
        };

        // URLs don't match; names are ambiguous → no suggestion
        Assert.Null(AzureRepositoryMatchSuggester.Suggest(remotes, projects, repos));
    }
}
