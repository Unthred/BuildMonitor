using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Suggests an Azure repository match from local Git remotes (never auto-attaches).</summary>
public static class AzureRepositoryMatchSuggester
{
    public sealed record Suggestion(
        string ProjectId,
        string ProjectName,
        string RepositoryId,
        string RepositoryName,
        string MatchReason);

    public static Suggestion? Suggest(
        IReadOnlyList<LocalGitRemote> remotes,
        IReadOnlyList<AzureProjectSummary> projects,
        IReadOnlyList<AzureRepositorySummary> repositories)
    {
        var remoteKeys = remotes
            .Select(r => AzureGitRemoteCanonicalizer.NormalizeComparableUrl(r.Url))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (remoteKeys.Length == 0)
        {
            return null;
        }

        var urlMatches = new List<(AzureRepositorySummary Repo, string Reason)>();
        foreach (var repo in repositories)
        {
            var candidates = new[] { repo.RemoteUrl, repo.WebUrl }
                .Select(AzureGitRemoteCanonicalizer.NormalizeComparableUrl)
                .Where(k => !string.IsNullOrWhiteSpace(k));

            foreach (var key in candidates)
            {
                if (remoteKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    urlMatches.Add((repo, "Exact Azure DevOps remote URL match"));
                    break;
                }
            }
        }

        if (urlMatches.Count == 1)
        {
            return ToSuggestion(urlMatches[0].Repo, projects, urlMatches[0].Reason);
        }

        if (urlMatches.Count > 1)
        {
            return null;
        }

        // Name-only match only when unambiguous across the provided repository list.
        var remoteRepoNames = remotes
            .Select(r => AzureGitRemoteCanonicalizer.TryParseAzureDevOpsRemote(r.Url, out var id) ? id.Repository : null)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (remoteRepoNames.Length != 1)
        {
            return null;
        }

        var name = remoteRepoNames[0]!;
        var nameMatches = repositories
            .Where(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nameMatches.Count != 1)
        {
            return null;
        }

        return ToSuggestion(nameMatches[0], projects, "Unambiguous repository name match");
    }

    private static Suggestion ToSuggestion(
        AzureRepositorySummary repo,
        IReadOnlyList<AzureProjectSummary> projects,
        string reason)
    {
        var projectName = repo.ProjectName;
        var projectId = repo.ProjectId;
        if (string.IsNullOrWhiteSpace(projectName))
        {
            var project = projects.FirstOrDefault(p =>
                string.Equals(p.Id, repo.ProjectId, StringComparison.OrdinalIgnoreCase));
            projectName = project?.Name ?? repo.ProjectId;
            projectId = project?.Id ?? repo.ProjectId;
        }

        return new Suggestion(projectId, projectName, repo.Id, repo.Name, reason);
    }
}
