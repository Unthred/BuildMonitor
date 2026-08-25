namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>Builds Azure DevOps build-results URLs (run page only — no stage/job fabrication).</summary>
public static class AzureDevOpsDeepLinkBuilder
{
    public static string BuildRunResultsUrl(string organizationUrl, string adoProjectIdOrName, long buildId)
    {
        var org = organizationUrl.TrimEnd('/');
        var project = Uri.EscapeDataString(adoProjectIdOrName.Trim());
        return $"{org}/{project}/_build/results?buildId={buildId}&view=results";
    }
}
