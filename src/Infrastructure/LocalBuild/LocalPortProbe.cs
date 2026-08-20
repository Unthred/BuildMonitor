using System.Net.Sockets;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class LocalPortProbe
{
    public static bool IsHttpEndpointOpen(string url, int timeoutMs = 500)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Port <= 0)
        {
            return false;
        }

        if (IsLoopbackHost(uri.Host))
        {
            return IsPortOpen("127.0.0.1", uri.Port, timeoutMs)
                || IsPortOpen("::1", uri.Port, timeoutMs);
        }

        return IsPortOpen(uri.Host, uri.Port, timeoutMs);
    }

    /// <summary>
    /// Single canonical user-facing URL for status panel, links, and notifications.
    /// Prefers launch-profile hostnames (especially HTTPS localhost) over runtime loopback IPs.
    /// </summary>
    public static string? ResolveCanonicalUserFacingUrl(
        string? runtimeUrl,
        IReadOnlyList<string> profileUrls)
    {
        if (string.IsNullOrWhiteSpace(runtimeUrl))
        {
            var preferred = SelectPreferredProfileUrl(profileUrls);
            return preferred is null ? null : ToAbsoluteUri(preferred);
        }

        var matchedProfile = FindMatchingProfileUrl(runtimeUrl, profileUrls);
        return matchedProfile is not null
            ? ToAbsoluteUri(matchedProfile)
            : ToAbsoluteUri(runtimeUrl);
    }

    /// <summary>
    /// When multiple probe/runtime URLs are open, pick the best canonical URL by profile priority.
    /// </summary>
    public static string? ResolveCanonicalUserFacingUrlFromOpenEndpoints(
        IReadOnlyList<string> openRuntimeUrls,
        IReadOnlyList<string> profileUrls)
    {
        if (openRuntimeUrls.Count == 0)
        {
            return ResolveCanonicalUserFacingUrl((string?)null, profileUrls);
        }

        var candidates = new List<string>();
        foreach (var runtimeUrl in openRuntimeUrls)
        {
            var matched = FindMatchingProfileUrl(runtimeUrl, profileUrls);
            candidates.Add(matched ?? runtimeUrl);
        }

        foreach (var profileUrl in OrderProfileUrlsByPriority(profileUrls))
        {
            if (openRuntimeUrls.Any(open => SameListenEndpoint(open, profileUrl)))
            {
                return ToAbsoluteUri(profileUrl);
            }
        }

        return ToAbsoluteUri(SelectPreferredCandidate(candidates));
    }

    public static string? SelectPreferredProfileUrl(IReadOnlyList<string> profileUrls)
    {
        var ordered = OrderProfileUrlsByPriority(profileUrls);
        return ordered.FirstOrDefault();
    }

    [Obsolete("Use ResolveCanonicalUserFacingUrl for user-facing URLs.")]
    public static string NormalizeBrowserUrl(string url) =>
        ResolveCanonicalUserFacingUrl(url, []) ?? url;

    [Obsolete("Use ResolveCanonicalUserFacingUrl for user-facing URLs.")]
    public static string NormalizeDisplayUrl(string url) =>
        ResolveCanonicalUserFacingUrl(url, []) ?? url;

    [Obsolete("Use ResolveCanonicalUserFacingUrl for user-facing URLs.")]
    public static string PreferProfileDisplayUrl(string runtimeUrl, IReadOnlyList<string> profileUrls) =>
        ResolveCanonicalUserFacingUrl(runtimeUrl, profileUrls) ?? runtimeUrl;

    public static bool SameListenEndpoint(string left, string right)
    {
        if (!Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
            || !Uri.TryCreate(right, UriKind.Absolute, out var rightUri))
        {
            return false;
        }

        return leftUri.Port == rightUri.Port
            && leftUri.Scheme.Equals(rightUri.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)
        || host.Equals("::1", StringComparison.OrdinalIgnoreCase);

    internal static int GetProfileUrlPriorityRank(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return 100;
        }

        var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isLocalhost = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        return (isHttps, isLocalhost) switch
        {
            (true, true) => 0,
            (true, false) => 1,
            (false, true) => 2,
            _ => 3
        };
    }

    internal static IReadOnlyList<string> OrderProfileUrlsByPriority(IReadOnlyList<string> profileUrls) =>
        profileUrls
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .OrderBy(GetProfileUrlPriorityRank)
            .ThenBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static string? FindMatchingProfileUrl(string runtimeUrl, IReadOnlyList<string> profileUrls)
    {
        foreach (var candidate in OrderProfileUrlsByPriority(profileUrls))
        {
            if (SameListenEndpoint(runtimeUrl, candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static string ToAbsoluteUri(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : url;
    }

    private static string SelectPreferredCandidate(IReadOnlyList<string> candidates)
    {
        return candidates
            .OrderBy(GetProfileUrlPriorityRank)
            .ThenBy(u => u, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static bool IsPortOpen(string host, int port, int timeoutMs)
    {
        try
        {
            var family = host.Contains(':', StringComparison.Ordinal)
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;

            using var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp)
            {
                Blocking = false
            };

            var connectResult = socket.BeginConnect(host, port, null, null);
            if (!connectResult.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                socket.Close();
                return false;
            }

            socket.EndConnect(connectResult);
            return socket.Connected;
        }
        catch
        {
            return false;
        }
    }
}
