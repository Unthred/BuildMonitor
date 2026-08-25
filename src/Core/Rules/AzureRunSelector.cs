using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Selects primary and attention Azure runs for a project poll cycle.</summary>
public static class AzureRunSelector
{
    public static bool IsActive(PipelineRunState state) =>
        state is PipelineRunState.NotStarted
            or PipelineRunState.InProgress
            or PipelineRunState.Canceling;

    /// <summary>
    /// From recent builds for one pipeline, pick the run that represents that pipeline:
    /// active preferred over completed; among completed, latest finish among relevant branches
    /// (or any branch if none match).
    /// </summary>
    public static AzurePipelineRunInfo? SelectPipelineRepresentative(
        IReadOnlyList<AzurePipelineRunInfo> recentRuns,
        IReadOnlyList<string> relevantBranches)
    {
        if (recentRuns.Count == 0)
        {
            return null;
        }

        var relevant = recentRuns
            .Where(r => IsRelevant(r.Branch, relevantBranches))
            .ToList();
        var pool = relevant.Count > 0 ? relevant : recentRuns.ToList();

        var active = pool
            .Where(r => IsActive(r.State))
            .OrderByDescending(r => r.StartedAtUtc ?? r.QueuedAtUtc)
            .FirstOrDefault();
        if (active is not null)
        {
            return active;
        }

        return pool
            .Where(r => r.State == PipelineRunState.Completed)
            .OrderByDescending(r => r.FinishedAtUtc ?? r.QueuedAtUtc)
            .FirstOrDefault()
            ?? pool.OrderByDescending(r => r.QueuedAtUtc).FirstOrDefault();
    }

    public static (AzurePipelineRunInfo? Primary, IReadOnlyList<AzurePipelineRunInfo> Attention) SelectPrimaryAndAttention(
        IReadOnlyList<AzurePipelineRunInfo> representatives,
        string? focusBranch)
    {
        if (representatives.Count == 0)
        {
            return (null, []);
        }

        AzurePipelineRunInfo? primary = null;
        if (!string.IsNullOrWhiteSpace(focusBranch))
        {
            primary = representatives
                .Where(r => string.Equals(r.Branch, focusBranch, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(SeverityRank)
                .ThenByDescending(r => IsActive(r.State))
                .ThenByDescending(r => r.StartedAtUtc ?? r.FinishedAtUtc ?? r.QueuedAtUtc)
                .FirstOrDefault();
        }

        primary ??= representatives
            .OrderByDescending(SeverityRank)
            .ThenByDescending(r => IsActive(r.State))
            .ThenByDescending(r => r.StartedAtUtc ?? r.FinishedAtUtc ?? r.QueuedAtUtc)
            .First();

        var attention = representatives
            .Where(r => !ReferenceEquals(r, primary) && IsAttentionWorthy(r))
            .OrderByDescending(SeverityRank)
            .ThenBy(r => r.PipelineDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (primary, attention);
    }

    public static bool IsAttentionWorthy(AzurePipelineRunInfo run)
    {
        if (IsActive(run.State))
        {
            return true;
        }

        if (run.State != PipelineRunState.Completed)
        {
            return false;
        }

        return run.Result is PipelineRunResult.Failed or PipelineRunResult.PartiallySucceeded;
    }

    public static int SeverityRank(AzurePipelineRunInfo run)
    {
        if (run.State == PipelineRunState.Completed && run.Result == PipelineRunResult.Failed)
        {
            return 400;
        }

        if (run.State == PipelineRunState.Completed && run.Result == PipelineRunResult.PartiallySucceeded)
        {
            return 300;
        }

        if (IsActive(run.State))
        {
            return 200;
        }

        if (run.State == PipelineRunState.Completed && run.Result == PipelineRunResult.Succeeded)
        {
            return 100;
        }

        return 0;
    }

    private static bool IsRelevant(string branch, IReadOnlyList<string> relevantBranches)
    {
        if (relevantBranches.Count == 0)
        {
            return true;
        }

        var shortName = AzureGitBranchNormalizer.ToShortName(branch) ?? branch;
        return relevantBranches.Any(b => string.Equals(b, shortName, StringComparison.OrdinalIgnoreCase));
    }
}
