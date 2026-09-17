namespace BuildMonitor.Core.Rules;

public enum VerificationProviderDeclineReason
{
    Claimed = 0,
    Unreachable = 1,
    NoExactMatch = 2,
    OperationUnsupported = 3
}

public sealed record VerificationProviderClaimDecision(
    bool Claimed,
    string? MatchedProjectRoot,
    VerificationProviderDeclineReason Reason,
    bool ExclusiveOwnership,
    string FallbackProviderName,
    string Report);

/// <summary>
/// Exact-root claim for the user-installed BuildMonitor adapter (WitherbyConnect PR 224 contract).
/// Parent, child, sibling, similarly named, and original-workspace paths are not claims.
/// </summary>
public static class VerificationProviderClaim
{
    public const string DirectFallbackName = "direct-dotnet";
    public const string AdapterSkillName = "buildmonitor-control-plane";

    public static VerificationProviderClaimDecision Evaluate(
        string worktreePath,
        IReadOnlyList<string> configuredProjectRoots,
        bool providerReachable,
        bool supportsRequestedOperation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentNullException.ThrowIfNull(configuredProjectRoots);

        if (!providerReachable)
        {
            return Decline(
                VerificationProviderDeclineReason.Unreachable,
                "BuildMonitor is unreachable; use the product repository direct fallback.");
        }

        string? matched = null;
        foreach (var root in configuredProjectRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (VerificationProviderPath.EqualsExact(worktreePath, root))
            {
                matched = VerificationProviderPath.Normalize(root);
                break;
            }
        }

        if (matched is null)
        {
            return Decline(
                VerificationProviderDeclineReason.NoExactMatch,
                "No configured project root matches this exact worktree; unconfigured is valid. Do not auto-configure.");
        }

        if (!supportsRequestedOperation)
        {
            return Decline(
                VerificationProviderDeclineReason.OperationUnsupported,
                "Exact worktree matched but the project does not support the requested verification operation.");
        }

        return new VerificationProviderClaimDecision(
            Claimed: true,
            MatchedProjectRoot: matched,
            Reason: VerificationProviderDeclineReason.Claimed,
            ExclusiveOwnership: true,
            FallbackProviderName: AdapterSkillName,
            Report: "BuildMonitor claimed this exact worktree and is available.");
    }

    private static VerificationProviderClaimDecision Decline(
        VerificationProviderDeclineReason reason,
        string report) =>
        new(
            Claimed: false,
            MatchedProjectRoot: null,
            Reason: reason,
            ExclusiveOwnership: false,
            FallbackProviderName: DirectFallbackName,
            Report: report);
}
