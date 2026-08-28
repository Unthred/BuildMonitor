using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Builds semantic per-column navigation for Azure PrimaryRun BUILDS rows.</summary>
public static class AzureBuildSourceNavigationBuilder
{
    public static AzureBuildSourceNavigation Build(
        AzurePipelineRunInfo primaryRun,
        AzureBuildNavigationContext context)
    {
        var runResultsUri = AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(
            context.OrganizationUrl,
            context.AdoProjectIdOrName,
            primaryRun.RunId);
        var runTarget = AzureBuildLinkTarget.Static(AzureBuildLinkKind.RunResults, runResultsUri);

        var statusTarget = NeedsFailureResolution(primaryRun)
            ? AzureBuildLinkTarget.FailureDetails()
            : AzureBuildLinkTarget.Static(AzureBuildLinkKind.RunResults, runResultsUri);

        AzureBuildFailureNavigationRequest? failureRequest = NeedsFailureResolution(primaryRun)
            ? new AzureBuildFailureNavigationRequest(
                context.ProjectId,
                context.ConnectionId,
                context.OrganizationUrl,
                context.AdoProjectIdOrName,
                primaryRun.RunId)
            : null;

        var pullRequestTarget = primaryRun.PullRequestNumber is int pr && pr > 0
            ? AzureBuildLinkTarget.Static(
                AzureBuildLinkKind.PullRequest,
                AzureDevOpsDeepLinkBuilder.BuildPullRequestUrl(
                    context.OrganizationUrl,
                    context.AdoProjectIdOrName,
                    context.RepositoryName,
                    pr))
            : AzureBuildLinkTarget.None;

        var (branchTarget, branchRequest) = TryBuildBranchNavigation(primaryRun, context, runResultsUri);

        return new AzureBuildSourceNavigation(
            statusTarget,
            runTarget,
            runTarget,
            pullRequestTarget,
            branchTarget,
            failureRequest,
            branchRequest);
    }

    private static bool NeedsFailureResolution(AzurePipelineRunInfo run) =>
        run.State == PipelineRunState.Completed
        && run.Result is PipelineRunResult.Failed or PipelineRunResult.PartiallySucceeded;

    private static (AzureBuildLinkTarget Target, AzureBuildBranchNavigationRequest? Request) TryBuildBranchNavigation(
        AzurePipelineRunInfo run,
        AzureBuildNavigationContext context,
        string runResultsUri)
    {
        var shortName = AzureGitBranchNormalizer.ToShortName(run.SourceBranchRef);
        if (string.IsNullOrWhiteSpace(shortName)
            || AzurePullRequestMetadata.IsPullRequestRef(run.SourceBranchRef))
        {
            return (AzureBuildLinkTarget.None, null);
        }

        var branchUrl = AzureDevOpsDeepLinkBuilder.BuildBranchUrl(
            context.OrganizationUrl,
            context.AdoProjectIdOrName,
            context.RepositoryName,
            shortName);

        int? trustworthyPr = run.PullRequestNumber is int pr && pr > 0 ? pr : null;
        var request = new AzureBuildBranchNavigationRequest(
            context.ProjectId,
            context.ConnectionId,
            context.OrganizationUrl,
            context.AdoProjectIdOrName,
            context.RepositoryId,
            context.RepositoryName,
            run.RunId,
            run.SourceBranchRef!.Trim(),
            AzureGitCommitIdValidator.Normalize(run.SourceVersion),
            trustworthyPr,
            branchUrl);

        return (AzureBuildLinkTarget.ResilientBranch(), request);
    }
}
