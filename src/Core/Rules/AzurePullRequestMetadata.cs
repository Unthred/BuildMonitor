using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Extracts a trustworthy Azure pull-request number and display branch from build list metadata.
/// Does not infer PR numbers from ordinary branch names (e.g. feature/327-fix).
/// </summary>
public static partial class AzurePullRequestMetadata
{
    [GeneratedRegex(@"^refs/pull/(?<id>\d+)/(?:merge|head)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PullRefRegex();

    public static int? TryResolveNumber(
        string? reason,
        string? sourceBranch,
        JsonElement? triggerInfo)
    {
        var fromTrigger = TryFromTriggerInfo(triggerInfo);
        if (fromTrigger is > 0)
        {
            return fromTrigger;
        }

        var isPullRequest = IsPullRequestReason(reason) || IsPullRequestRef(sourceBranch);
        if (!isPullRequest)
        {
            return null;
        }

        return TryFromPullRef(sourceBranch);
    }

    public static string ResolveDisplayBranch(
        string? sourceBranch,
        int? pullRequestNumber,
        JsonElement? triggerInfo)
    {
        var branchRef = ResolveSourceBranchRef(sourceBranch, triggerInfo);
        if (!string.IsNullOrWhiteSpace(branchRef))
        {
            return AzureGitBranchNormalizer.ToShortName(branchRef) ?? branchRef.Trim();
        }

        if (IsPullRequestRef(sourceBranch))
        {
            return pullRequestNumber is > 0
                ? $"PR #{pullRequestNumber.Value}"
                : "PR";
        }

        return AzureGitBranchNormalizer.ToShortName(sourceBranch)
            ?? (string.IsNullOrWhiteSpace(sourceBranch) ? "unknown" : sourceBranch.Trim());
    }

    /// <summary>Real navigable branch ref; null when only a PR merge ref is known.</summary>
    public static string? ResolveSourceBranchRef(string? sourceBranch, JsonElement? triggerInfo)
    {
        var fromTrigger = TryTriggerSourceBranch(triggerInfo);
        if (!string.IsNullOrWhiteSpace(fromTrigger) && !IsPullRequestRef(fromTrigger))
        {
            return fromTrigger.Trim();
        }

        if (IsPullRequestRef(sourceBranch))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(sourceBranch) ? null : sourceBranch.Trim();
    }

    public static bool IsPullRequestReason(string? reason) =>
        string.Equals(reason, "pullRequest", StringComparison.OrdinalIgnoreCase);

    public static bool IsPullRequestRef(string? sourceBranch)
    {
        if (string.IsNullOrWhiteSpace(sourceBranch))
        {
            return false;
        }

        return PullRefRegex().IsMatch(sourceBranch.Trim());
    }

    public static int? TryFromPullRef(string? sourceBranch)
    {
        if (string.IsNullOrWhiteSpace(sourceBranch))
        {
            return null;
        }

        var match = PullRefRegex().Match(sourceBranch.Trim());
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["id"].Value, out var id) && id > 0 ? id : null;
    }

    private static int? TryFromTriggerInfo(JsonElement? triggerInfo)
    {
        if (triggerInfo is not { ValueKind: JsonValueKind.Object } info)
        {
            return null;
        }

        foreach (var prop in info.EnumerateObject())
        {
            if (!IsTrustedPrNumberKey(prop.Name))
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n) && n > 0)
            {
                return n;
            }

            var text = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
            if (int.TryParse(text, out var parsed) && parsed > 0)
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? TryTriggerSourceBranch(JsonElement? triggerInfo)
    {
        if (triggerInfo is not { ValueKind: JsonValueKind.Object } info)
        {
            return null;
        }

        foreach (var prop in info.EnumerateObject())
        {
            if (!IsTrustedPrSourceBranchKey(prop.Name))
            {
                continue;
            }

            var text = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
            if (!string.IsNullOrWhiteSpace(text) && !IsPullRequestRef(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool IsTrustedPrNumberKey(string name) =>
        name.Equals("pr.number", StringComparison.OrdinalIgnoreCase)
        || name.Equals("pullRequestId", StringComparison.OrdinalIgnoreCase)
        || name.Equals("system.pullRequest.pullRequestId", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".pullRequestId", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedPrSourceBranchKey(string name) =>
        name.Equals("pr.sourceBranch", StringComparison.OrdinalIgnoreCase)
        || name.Equals("system.pullRequest.sourceBranch", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".sourceBranch", StringComparison.OrdinalIgnoreCase)
           && name.Contains("pullRequest", StringComparison.OrdinalIgnoreCase);
}
