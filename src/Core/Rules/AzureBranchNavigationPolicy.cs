namespace BuildMonitor.Core.Rules;

/// <summary>Authoritative branch-ref existence outcome for lazy navigation.</summary>
public enum AzureBranchRefExistence
{
    Unknown = 0,
    Exists = 1,
    Deleted = 2
}

/// <summary>Pure destination selection for resilient Azure Branch navigation (#100).</summary>
public static class AzureBranchNavigationPolicy
{
    public static Uri SelectDestination(
        AzureBranchRefExistence existence,
        string branchUrlFallback,
        string runResultsUrl,
        string? pullRequestUrl,
        string? commitUrl)
    {
        if (existence == AzureBranchRefExistence.Exists)
        {
            return RequireUri(branchUrlFallback);
        }

        if (existence == AzureBranchRefExistence.Unknown)
        {
            return RequireUri(branchUrlFallback);
        }

        if (!string.IsNullOrWhiteSpace(pullRequestUrl))
        {
            return RequireUri(pullRequestUrl);
        }

        if (!string.IsNullOrWhiteSpace(commitUrl))
        {
            return RequireUri(commitUrl);
        }

        return RequireUri(runResultsUrl);
    }

    public static bool ShouldCacheOutcome(AzureBranchRefExistence existence) =>
        existence is AzureBranchRefExistence.Exists or AzureBranchRefExistence.Deleted;

    public static bool IsLongLivedCache(AzureBranchRefExistence existence) =>
        existence == AzureBranchRefExistence.Deleted;

    private static Uri RequireUri(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri
            : throw new ArgumentException("Expected absolute http(s) URI.", nameof(url));
}
