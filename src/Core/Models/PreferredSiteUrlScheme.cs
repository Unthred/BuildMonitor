namespace BuildMonitor.Core.Models;

/// <summary>Which launch-profile listen URL to prefer for status panel / browser open.</summary>
public enum PreferredSiteUrlScheme
{
    /// <summary>Prefer HTTPS when the profile (or open endpoints) include it.</summary>
    Auto = 0,
    /// <summary>Prefer HTTPS endpoints only when available; otherwise fall back.</summary>
    Https = 1,
    /// <summary>Prefer HTTP endpoints only when available; otherwise fall back.</summary>
    Http = 2
}

public static class PreferredSiteUrlSchemeDisplay
{
    public static string ToLabel(PreferredSiteUrlScheme scheme) =>
        scheme switch
        {
            PreferredSiteUrlScheme.Https => "HTTPS",
            PreferredSiteUrlScheme.Http => "HTTP",
            _ => "Auto (prefer HTTPS)"
        };
}
