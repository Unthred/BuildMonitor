namespace BuildMonitor.Core.Rules;

/// <summary>Validates and normalizes Azure DevOps organisation base URLs.</summary>
public static class AzureOrganizationUrl
{
    public static bool TryNormalize(string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Organisation URL is required.";
            return false;
        }

        var trimmed = raw.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = "Organisation URL must be an absolute http(s) URL (for example https://dev.azure.com/contoso).";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Organisation URL should use https.";
            return false;
        }

        normalized = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
        return true;
    }
}
