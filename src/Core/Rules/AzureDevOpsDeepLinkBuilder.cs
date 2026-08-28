namespace BuildMonitor.Core.Rules;

/// <summary>Builds Azure DevOps web URLs for builds, logs, PRs, and branches.</summary>
public static class AzureDevOpsDeepLinkBuilder
{
    public static string BuildRunResultsUrl(string organizationUrl, string adoProjectIdOrName, long buildId) =>
        $"{NormalizeOrg(organizationUrl)}/{EscapeProject(adoProjectIdOrName)}/_build/results?buildId={buildId}&view=results";

    public static string BuildRunTaskLogsUrl(
        string organizationUrl,
        string adoProjectIdOrName,
        long buildId,
        Guid jobId,
        Guid taskId) =>
        $"{NormalizeOrg(organizationUrl)}/{EscapeProject(adoProjectIdOrName)}/_build/results?buildId={buildId}&view=logs&j={jobId:D}&t={taskId:D}";

    /// <summary>Job-level logs when no failed task is available.</summary>
    public static string BuildRunJobLogsUrl(
        string organizationUrl,
        string adoProjectIdOrName,
        long buildId,
        Guid jobId) =>
        $"{NormalizeOrg(organizationUrl)}/{EscapeProject(adoProjectIdOrName)}/_build/results?buildId={buildId}&view=logs&j={jobId:D}";

    public static string BuildPullRequestUrl(
        string organizationUrl,
        string adoProjectIdOrName,
        string repositoryName,
        int pullRequestId) =>
        $"{NormalizeOrg(organizationUrl)}/{EscapeProject(adoProjectIdOrName)}/_git/{EscapePathSegment(repositoryName)}/pullrequest/{pullRequestId}";

    /// <summary>Branch browser view using <c>version=GB{branch}</c>.</summary>
    public static string BuildBranchUrl(
        string organizationUrl,
        string adoProjectIdOrName,
        string repositoryName,
        string branchShortName) =>
        $"{NormalizeOrg(organizationUrl)}/{EscapeProject(adoProjectIdOrName)}/_git/{EscapePathSegment(repositoryName)}?version=GB{Uri.EscapeDataString(branchShortName)}";

    private static string NormalizeOrg(string organizationUrl) => organizationUrl.TrimEnd('/');

    private static string EscapeProject(string adoProjectIdOrName) =>
        Uri.EscapeDataString(adoProjectIdOrName.Trim());

    private static string EscapePathSegment(string segment) =>
        Uri.EscapeDataString(segment.Trim());
}
