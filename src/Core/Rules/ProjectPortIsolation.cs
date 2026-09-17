using BuildMonitor.Core.Abstractions;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Runtime port assignment that never edits source-controlled launchSettings.json.
/// Explicit ApplicationUrl values stay stable; colliding launch-profile ports can be
/// offset and persisted on the initiating project only.
/// </summary>
public static class ProjectPortIsolation
{
    /// <summary>
    /// Offset applied when two configured projects would otherwise share launch-profile ports.
    /// 44333 + 16 = 44349, which keeps HTTPS worktree sites in the same range.
    /// </summary>
    public const int SiblingPortOffset = 16;

    public static IReadOnlyList<string> SplitApplicationUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string JoinApplicationUrl(IReadOnlyList<string> urls) =>
        string.Join(';', urls.Where(u => !string.IsNullOrWhiteSpace(u)));

    public static bool TryGetPort(string url, out int port)
    {
        port = 0;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Port > 0 && (port = uri.Port) > 0;
    }

    public static IReadOnlyList<int> ExtractPorts(IEnumerable<string> urls)
    {
        var ports = new List<int>();
        foreach (var url in urls)
        {
            if (TryGetPort(url, out var port) && !ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        return ports;
    }

    public static IReadOnlyList<string> OffsetUrls(IReadOnlyList<string> urls, int offset)
    {
        if (offset == 0)
        {
            return urls;
        }

        var shifted = new List<string>(urls.Count);
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Port <= 0)
            {
                shifted.Add(url);
                continue;
            }

            var builder = new UriBuilder(uri)
            {
                Port = uri.Port + offset
            };
            shifted.Add(builder.Uri.GetComponents(
                UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
                UriFormat.Unescaped).TrimEnd('/'));
        }

        return shifted;
    }

    public static bool TryFindClaimingPeer(
        IReadOnlyList<string> candidateUrls,
        IReadOnlyList<PeerProjectListenInfo> peers,
        out PeerProjectListenInfo peer,
        out int port)
    {
        var ports = ExtractPorts(candidateUrls);
        foreach (var candidatePort in ports)
        {
            foreach (var candidate in peers)
            {
                if (ExtractPorts(candidate.ListenUrls).Contains(candidatePort))
                {
                    peer = candidate;
                    port = candidatePort;
                    return true;
                }
            }
        }

        peer = null!;
        port = 0;
        return false;
    }

    public static ProjectPortDecision Decide(
        string? configuredOverride,
        IReadOnlyList<string> profileUrls,
        IReadOnlyList<PeerProjectListenInfo> peers)
    {
        var explicitUrls = SplitApplicationUrl(configuredOverride);
        var candidates = explicitUrls.Count > 0 ? explicitUrls : profileUrls;
        if (candidates.Count == 0)
        {
            return ProjectPortDecision.Allow(applicationUrl: null, persist: false);
        }

        if (TryFindClaimingPeer(candidates, peers, out var peer, out var port))
        {
            if (explicitUrls.Count > 0)
            {
                return ProjectPortDecision.Block(peer, port);
            }

            var offset = SiblingPortOffset;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var isolated = OffsetUrls(profileUrls, offset);
                if (!TryFindClaimingPeer(isolated, peers, out _, out _))
                {
                    return ProjectPortDecision.Allow(JoinApplicationUrl(isolated), persist: true);
                }

                offset += SiblingPortOffset;
            }

            return ProjectPortDecision.Block(peer, port);
        }

        return ProjectPortDecision.Allow(JoinApplicationUrl(candidates), persist: false);
    }
}

public sealed record ProjectPortDecision(
    bool CanStart,
    string? ApplicationUrl,
    bool PersistApplicationUrl,
    PeerProjectListenInfo? BlockingPeer,
    int BlockingPort)
{
    public static ProjectPortDecision Allow(string? applicationUrl, bool persist) =>
        new(true, applicationUrl, persist, null, 0);

    public static ProjectPortDecision Block(PeerProjectListenInfo peer, int port) =>
        new(false, null, false, peer, port);
}
