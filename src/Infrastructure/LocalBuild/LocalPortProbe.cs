using System.Net.Sockets;
using BuildMonitor.Core.Models;

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
        IReadOnlyList<string> profileUrls,
        PreferredSiteUrlScheme preference = PreferredSiteUrlScheme.Auto)
    {
        if (string.IsNullOrWhiteSpace(runtimeUrl))
        {
            var preferred = SelectPreferredProfileUrl(profileUrls, preference);
            return preferred is null ? null : ToAbsoluteUri(preferred);
        }

        var matchedProfile = FindMatchingProfileUrl(runtimeUrl, profileUrls, preference);
        return matchedProfile is not null
            ? ToAbsoluteUri(matchedProfile)
            : ToAbsoluteUri(runtimeUrl);
    }

    /// <summary>
    /// When multiple probe/runtime URLs are open, pick the best canonical URL by preference + profile priority.
    /// </summary>
    public static string? ResolveCanonicalUserFacingUrlFromOpenEndpoints(
        IReadOnlyList<string> openRuntimeUrls,
        IReadOnlyList<string> profileUrls,
        PreferredSiteUrlScheme preference = PreferredSiteUrlScheme.Auto)
    {
        if (openRuntimeUrls.Count == 0)
        {
            return ResolveCanonicalUserFacingUrl((string?)null, profileUrls, preference);
        }

        var candidates = new List<string>();
        foreach (var runtimeUrl in openRuntimeUrls)
        {
            var matched = FindMatchingProfileUrl(runtimeUrl, profileUrls, preference);
            candidates.Add(matched ?? runtimeUrl);
        }

        foreach (var profileUrl in OrderProfileUrlsByPriority(profileUrls, preference))
        {
            if (openRuntimeUrls.Any(open => SameListenEndpoint(open, profileUrl)))
            {
                return ToAbsoluteUri(profileUrl);
            }
        }

        return ToAbsoluteUri(SelectPreferredCandidate(candidates, preference));
    }

    public static string? SelectPreferredProfileUrl(
        IReadOnlyList<string> profileUrls,
        PreferredSiteUrlScheme preference = PreferredSiteUrlScheme.Auto)
    {
        var ordered = OrderProfileUrlsByPriority(profileUrls, preference);
        return ordered.FirstOrDefault();
    }

    /// <summary>
    /// True when a preferred-scheme profile URL exists but none of that scheme is open yet
    /// (caller should keep waiting instead of locking onto HTTP).
    /// </summary>
    public static bool ShouldWaitForPreferredScheme(
        IReadOnlyList<string> openRuntimeUrls,
        IReadOnlyList<string> profileUrls,
        PreferredSiteUrlScheme preference,
        bool graceExpired)
    {
        if (graceExpired || profileUrls.Count == 0)
        {
            return false;
        }

        var preferred = SelectPreferredProfileUrl(profileUrls, preference);
        if (preferred is null)
        {
            return false;
        }

        if (openRuntimeUrls.Any(open => SameListenEndpoint(open, preferred)))
        {
            return false;
        }

        // Preferred URL not open yet — wait only if we would otherwise pick a different scheme.
        if (openRuntimeUrls.Count == 0)
        {
            return true;
        }

        var fallback = ResolveCanonicalUserFacingUrlFromOpenEndpoints(
            openRuntimeUrls,
            profileUrls,
            preference);
        if (fallback is null)
        {
            return true;
        }

        return !SameListenEndpoint(fallback, preferred)
            && GetSchemeRank(preferred, preference) < GetSchemeRank(fallback, preference);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is a better user-facing URL than <paramref name="current"/>.
    /// </summary>
    public static bool IsBetterCanonicalUrl(
        string candidate,
        string? current,
        IReadOnlyList<string> profileUrls,
        PreferredSiteUrlScheme preference)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return true;
        }

        if (SameListenEndpoint(candidate, current))
        {
            // Prefer profile hostname spelling when scheme/port match.
            var candidateMatched = FindMatchingProfileUrl(candidate, profileUrls, preference);
            var currentMatched = FindMatchingProfileUrl(current, profileUrls, preference);
            if (candidateMatched is not null && currentMatched is null)
            {
                return true;
            }

            return false;
        }

        return GetSchemeRank(candidate, preference) < GetSchemeRank(current, preference);
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

    internal static int GetProfileUrlPriorityRank(
        string url,
        PreferredSiteUrlScheme preference = PreferredSiteUrlScheme.Auto)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return 100;
        }

        var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isLocalhost = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        return preference switch
        {
            PreferredSiteUrlScheme.Https => isHttps
                ? (isLocalhost ? 0 : 1)
                : (isLocalhost ? 50 : 51),
            PreferredSiteUrlScheme.Http => !isHttps
                ? (isLocalhost ? 0 : 1)
                : (isLocalhost ? 50 : 51),
            _ => (isHttps, isLocalhost) switch
            {
                (true, true) => 0,
                (true, false) => 1,
                (false, true) => 2,
                _ => 3
            }
        };
    }

    internal static IReadOnlyList<string> OrderProfileUrlsByPriority(
        IReadOnlyList<string> profileUrls,
        PreferredSiteUrlScheme preference = PreferredSiteUrlScheme.Auto) =>
        profileUrls
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .OrderBy(u => GetProfileUrlPriorityRank(u, preference))
            .ThenBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static string? FindMatchingProfileUrl(
        string runtimeUrl,
        IReadOnlyList<string> profileUrls,
        PreferredSiteUrlScheme preference = PreferredSiteUrlScheme.Auto)
    {
        foreach (var candidate in OrderProfileUrlsByPriority(profileUrls, preference))
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

    private static int GetSchemeRank(string url, PreferredSiteUrlScheme preference) =>
        GetProfileUrlPriorityRank(url, preference);

    private static string SelectPreferredCandidate(
        IReadOnlyList<string> candidates,
        PreferredSiteUrlScheme preference) =>
        candidates
            .OrderBy(u => GetProfileUrlPriorityRank(u, preference))
            .ThenBy(u => u, StringComparer.OrdinalIgnoreCase)
            .First();

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
