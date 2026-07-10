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

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IsPortOpen("127.0.0.1", uri.Port, timeoutMs)
                || IsPortOpen("::1", uri.Port, timeoutMs);
        }

        return IsPortOpen(uri.Host, uri.Port, timeoutMs);
    }

    public static string NormalizeBrowserUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        // Dev HTTPS certs are issued for localhost — rewriting to 127.0.0.1 breaks TLS in the browser.
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        if (!uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        return new UriBuilder(uri) { Host = "127.0.0.1" }.Uri.AbsoluteUri;
    }

    /// <summary>Human-friendly URL for status panel / tooltips (localhost instead of 127.0.0.1).</summary>
    public static string NormalizeDisplayUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return new UriBuilder(uri) { Host = "localhost" }.Uri.AbsoluteUri;
        }

        return uri.AbsoluteUri;
    }

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

    public static string PreferProfileDisplayUrl(string runtimeUrl, IReadOnlyList<string> profileUrls)
    {
        if (!Uri.TryCreate(runtimeUrl, UriKind.Absolute, out var runtime))
        {
            return runtimeUrl;
        }

        foreach (var candidate in profileUrls)
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var profile))
            {
                continue;
            }

            if (!SameListenEndpoint(runtimeUrl, candidate))
            {
                continue;
            }

            if (profile.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeDisplayUrl(candidate);
            }
        }

        return runtimeUrl;
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
