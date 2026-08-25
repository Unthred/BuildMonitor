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
    /// Presentation representative for one pipeline: any active run wins (all branches),
    /// otherwise the newest completed run overall so a just-finished non-health-scope run
    /// is not replaced by an older default-branch success.
    /// </summary>
    public static AzurePipelineRunInfo? SelectDisplayRepresentative(
        IReadOnlyList<AzurePipelineRunInfo> recentRuns)
    {
        if (recentRuns.Count == 0)
        {
            return null;
        }

        var active = recentRuns
            .Where(r => IsActive(r.State))
            .OrderByDescending(r => r.StartedAtUtc ?? r.QueuedAtUtc)
            .ThenByDescending(r => r.RunId)
            .FirstOrDefault();
        if (active is not null)
        {
            return active;
        }

        return recentRuns
            .Where(r => r.State == PipelineRunState.Completed)
            .OrderByDescending(r => r.FinishedAtUtc ?? r.QueuedAtUtc)
            .ThenByDescending(r => r.RunId)
            .FirstOrDefault()
            ?? recentRuns.OrderByDescending(r => r.QueuedAtUtc).ThenByDescending(r => r.RunId).FirstOrDefault();
    }

    /// <summary>
    /// Pipeline current-state representative for tray health: same selection as display
    /// (any active run, else newest completed overall). PR / non-default branches are real
    /// health signals. Branch relevance is not used to suppress a newer failure in favour of
    /// an older default-branch success; historical poisoning is avoided by always taking the
    /// newest meaningful run for the selected pipeline.
    /// </summary>
    public static AzurePipelineRunInfo? SelectHealthRepresentative(
        IReadOnlyList<AzurePipelineRunInfo> recentRuns,
        IReadOnlyList<string> relevantBranches)
    {
        _ = relevantBranches;
        return SelectDisplayRepresentative(recentRuns);
    }

    /// <summary>
    /// Legacy alias for display selection (branch list ignored for presentation).
    /// Prefer <see cref="SelectDisplayRepresentative"/> / <see cref="SelectHealthRepresentative"/>.
    /// </summary>
    public static AzurePipelineRunInfo? SelectPipelineRepresentative(
        IReadOnlyList<AzurePipelineRunInfo> recentRuns,
        IReadOnlyList<string> relevantBranches)
    {
        _ = relevantBranches;
        return SelectDisplayRepresentative(recentRuns);
    }

    /// <summary>
    /// When the display run is active, surface the most recent failed/partial completed run
    /// from the same pipeline (same branch preferred) for compact attention — not history.
    /// </summary>
    public static AzurePipelineRunInfo? SelectPreviousFailureAttention(
        IReadOnlyList<AzurePipelineRunInfo> recentRuns,
        AzurePipelineRunInfo? displayRun)
    {
        if (displayRun is null || !IsActive(displayRun.State))
        {
            return null;
        }

        return recentRuns
            .Where(r => r.RunId != displayRun.RunId)
            .Where(r => r.State == PipelineRunState.Completed)
            .Where(r => r.Result is PipelineRunResult.Failed or PipelineRunResult.PartiallySucceeded)
            .OrderByDescending(r => string.Equals(r.Branch, displayRun.Branch, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.FinishedAtUtc ?? r.QueuedAtUtc)
            .ThenByDescending(r => r.RunId)
            .FirstOrDefault();
    }

    public static (AzurePipelineRunInfo? Primary, IReadOnlyList<AzurePipelineRunInfo> Attention) SelectPrimaryAndAttention(
        IReadOnlyList<AzurePipelineRunInfo> representatives,
        string? focusBranch)
    {
        if (representatives.Count == 0)
        {
            return (null, []);
        }

        // Presentation: any active pipeline run beats completed severity ranks.
        AzurePipelineRunInfo? primary = representatives
            .Where(r => IsActive(r.State))
            .OrderByDescending(r => r.StartedAtUtc ?? r.QueuedAtUtc)
            .ThenByDescending(r => r.RunId)
            .FirstOrDefault();

        if (primary is null && !string.IsNullOrWhiteSpace(focusBranch))
        {
            primary = representatives
                .Where(r => string.Equals(r.Branch, focusBranch, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(SeverityRank)
                .ThenByDescending(r => r.StartedAtUtc ?? r.FinishedAtUtc ?? r.QueuedAtUtc)
                .ThenByDescending(r => r.RunId)
                .FirstOrDefault();
        }

        primary ??= representatives
            .OrderByDescending(SeverityRank)
            .ThenByDescending(r => r.StartedAtUtc ?? r.FinishedAtUtc ?? r.QueuedAtUtc)
            .ThenByDescending(r => r.RunId)
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
}
