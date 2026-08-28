namespace BuildMonitor.Core.Rules;

/// <summary>Validates URIs allowed through project link navigation.</summary>
public static class HttpUriNavigationValidator
{
    public static bool IsAllowedNavigationUri(Uri? uri) =>
        uri is not null
        && uri.IsAbsoluteUri
        && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    public static bool TryParseAllowed(string? uriText, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(uriText)
            || !Uri.TryCreate(uriText.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!IsAllowedNavigationUri(parsed))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
