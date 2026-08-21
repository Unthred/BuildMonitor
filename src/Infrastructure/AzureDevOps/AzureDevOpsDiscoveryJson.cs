using System.Text.Json;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.AzureDevOps;

internal static class AzureDevOpsDiscoveryJson
{
    public static IReadOnlyList<AzureProjectSummary> ParseProjects(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Projects response missing value array.");
        }

        var list = new List<AzureProjectSummary>();
        foreach (var item in value.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? string.Empty;
            var name = item.GetProperty("name").GetString() ?? string.Empty;
            var description = item.TryGetProperty("description", out var desc) ? desc.GetString() : null;
            var state = item.TryGetProperty("state", out var st) ? st.GetString() : null;
            list.Add(new AzureProjectSummary(id, name, description, state));
        }

        return list;
    }

    public static IReadOnlyList<AzureRepositorySummary> ParseRepositories(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Repositories response missing value array.");
        }

        var list = new List<AzureRepositorySummary>();
        foreach (var item in value.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? string.Empty;
            var name = item.GetProperty("name").GetString() ?? string.Empty;
            var projectId = string.Empty;
            var projectName = string.Empty;
            if (item.TryGetProperty("project", out var project))
            {
                projectId = project.TryGetProperty("id", out var pid) ? pid.GetString() ?? string.Empty : string.Empty;
                projectName = project.TryGetProperty("name", out var pname) ? pname.GetString() ?? string.Empty : string.Empty;
            }

            var remoteUrl = item.TryGetProperty("remoteUrl", out var remote) ? remote.GetString() : null;
            var webUrl = item.TryGetProperty("webUrl", out var web) ? web.GetString() : remoteUrl;
            var defaultBranch = item.TryGetProperty("defaultBranch", out var branch) ? branch.GetString() : null;
            list.Add(new AzureRepositorySummary(
                id,
                name,
                projectId,
                projectName,
                remoteUrl,
                webUrl,
                defaultBranch,
                AzureGitBranchNormalizer.ToShortName(defaultBranch)));
        }

        return list;
    }

    public static IReadOnlyList<AzurePipelineSummary> ParsePipelines(
        string json,
        string organizationUrl,
        string projectIdOrName,
        string? filterRepositoryId)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Build definitions response missing value array.");
        }

        var list = new List<AzurePipelineSummary>();
        foreach (var item in value.EnumerateArray())
        {
            var definitionId = item.GetProperty("id").GetInt32();
            var displayName = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? $"Pipeline {definitionId}" : $"Pipeline {definitionId}";
            var queueStatus = item.TryGetProperty("queueStatus", out var qs) ? qs.GetString() : null;
            var isEnabled = !string.Equals(queueStatus, "disabled", StringComparison.OrdinalIgnoreCase)
                && !(item.TryGetProperty("quality", out var quality) && string.Equals(quality.GetString(), "disabled", StringComparison.OrdinalIgnoreCase));

            string? repositoryId = null;
            string? repositoryName = null;
            string? repositoryType = null;
            if (item.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.Object)
            {
                repositoryId = repo.TryGetProperty("id", out var rid) ? rid.GetString() : null;
                repositoryName = repo.TryGetProperty("name", out var rname) ? rname.GetString() : null;
                repositoryType = repo.TryGetProperty("type", out var rtype) ? rtype.GetString() : null;
            }

            if (!string.IsNullOrWhiteSpace(filterRepositoryId)
                && !string.IsNullOrWhiteSpace(repositoryId)
                && !string.Equals(repositoryId, filterRepositoryId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = item.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
            var webUrl = $"{organizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(projectIdOrName)}/_build?definitionId={definitionId}";
            var triggerBranches = ParseTriggerBranches(item);

            list.Add(new AzurePipelineSummary(
                definitionId,
                displayName,
                isEnabled,
                repositoryId,
                repositoryName,
                repositoryType,
                path,
                webUrl,
                triggerBranches));
        }

        return list;
    }

    public static string? ParseAuthenticatedUserDisplayName(string connectionDataJson)
    {
        using var doc = JsonDocument.Parse(connectionDataJson);
        if (doc.RootElement.TryGetProperty("authenticatedUser", out var user)
            && user.TryGetProperty("providerDisplayName", out var display))
        {
            return display.GetString();
        }

        if (doc.RootElement.TryGetProperty("authenticatedUser", out var user2)
            && user2.TryGetProperty("customDisplayName", out var custom))
        {
            return custom.GetString();
        }

        return null;
    }

    private static IReadOnlyList<string> ParseTriggerBranches(JsonElement definition)
    {
        if (!definition.TryGetProperty("triggers", out var triggers) || triggers.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var branches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trigger in triggers.EnumerateArray())
        {
            if (!trigger.TryGetProperty("branchFilters", out var filters) || filters.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var filter in filters.EnumerateArray())
            {
                var raw = filter.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                // CI filters often look like "+refs/heads/main" or "-refs/heads/feature/*"
                var trimmed = raw.Trim();
                if (trimmed.StartsWith('+') || trimmed.StartsWith('-'))
                {
                    trimmed = trimmed[1..];
                }

                var shortName = AzureGitBranchNormalizer.ToShortName(trimmed);
                if (!string.IsNullOrWhiteSpace(shortName) && !shortName.Contains('*', StringComparison.Ordinal))
                {
                    branches.Add(shortName);
                }
            }
        }

        return branches.OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
