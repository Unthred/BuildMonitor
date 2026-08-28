using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Stable semantic equality for Azure BUILDS navigation (ignores record identity).</summary>
public static class AzureBuildSourceNavigationSemanticEqual
{
    public static bool Equals(AzureBuildSourceNavigation? left, AzureBuildSourceNavigation? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return LinkTargetEqual(left.Status, right.Status)
            && LinkTargetEqual(left.Run, right.Run)
            && LinkTargetEqual(left.BuildNumber, right.BuildNumber)
            && LinkTargetEqual(left.PullRequest, right.PullRequest)
            && LinkTargetEqual(left.Branch, right.Branch)
            && FailureRequestEqual(left.FailureRequest, right.FailureRequest);
    }

    public static bool LinkTargetEqual(AzureBuildLinkTarget left, AzureBuildLinkTarget right) =>
        left.Kind == right.Kind
        && string.Equals(left.Uri, right.Uri, StringComparison.Ordinal);

    public static bool FailureRequestEqual(
        AzureBuildFailureNavigationRequest? left,
        AzureBuildFailureNavigationRequest? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.RunId == right.RunId
            && string.Equals(left.ProjectId, right.ProjectId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ConnectionId, right.ConnectionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.OrganizationUrl, right.OrganizationUrl, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.AdoProjectIdOrName, right.AdoProjectIdOrName, StringComparison.OrdinalIgnoreCase);
    }
}
