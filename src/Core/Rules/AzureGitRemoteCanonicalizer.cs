namespace BuildMonitor.Core.Rules;

/// <summary>Canonicalises Azure DevOps Git remote URLs for association matching.</summary>
public static class AzureGitRemoteCanonicalizer
{
    public sealed record AzureRemoteIdentity(
        string Organization,
        string Project,
        string Repository,
        string? CanonicalHttpsUrl);

    public static bool TryParseAzureDevOpsRemote(string? remoteUrl, out AzureRemoteIdentity identity)
    {
        identity = null!;
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        var raw = remoteUrl.Trim();
        if (raw.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[..^4];
        }

        // SSH: git@ssh.dev.azure.com:v3/{org}/{project}/{repo}
        if (raw.StartsWith("git@ssh.dev.azure.com:", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("git@vs-ssh.visualstudio.com:", StringComparison.OrdinalIgnoreCase))
        {
            var path = raw[(raw.IndexOf(':') + 1)..].TrimStart('/');
            if (path.StartsWith("v3/", StringComparison.OrdinalIgnoreCase))
            {
                path = path[3..];
            }

            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                identity = new AzureRemoteIdentity(
                    parts[0],
                    parts[1],
                    parts[2],
                    $"https://dev.azure.com/{parts[0]}/{parts[1]}/_git/{parts[2]}");
                return true;
            }

            return false;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // https://dev.azure.com/{org}/{project}/_git/{repo}
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var gitIndex = Array.FindIndex(segments, s => s.Equals("_git", StringComparison.OrdinalIgnoreCase));
            if (gitIndex >= 2 && gitIndex + 1 < segments.Length)
            {
                var org = segments[0];
                var project = Uri.UnescapeDataString(segments[1]);
                var repo = Uri.UnescapeDataString(segments[gitIndex + 1]);
                identity = new AzureRemoteIdentity(
                    org,
                    project,
                    repo,
                    $"https://dev.azure.com/{org}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(repo)}");
                return true;
            }
        }

        // https://{org}.visualstudio.com/{project}/_git/{repo} or DefaultCollection/...
        if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            var org = uri.Host.Split('.')[0];
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var gitIndex = Array.FindIndex(segments, s => s.Equals("_git", StringComparison.OrdinalIgnoreCase));
            if (gitIndex >= 1 && gitIndex + 1 < segments.Length)
            {
                var project = Uri.UnescapeDataString(segments[gitIndex - 1]);
                var repo = Uri.UnescapeDataString(segments[gitIndex + 1]);
                identity = new AzureRemoteIdentity(
                    org,
                    project,
                    repo,
                    $"https://dev.azure.com/{org}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(repo)}");
                return true;
            }
        }

        return false;
    }

    public static string? NormalizeComparableUrl(string? url)
    {
        if (!TryParseAzureDevOpsRemote(url, out var identity))
        {
            return null;
        }

        return $"{identity.Organization}/{identity.Project}/{identity.Repository}".ToLowerInvariant();
    }
}
