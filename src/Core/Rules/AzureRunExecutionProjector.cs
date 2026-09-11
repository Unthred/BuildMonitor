using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Pure presentation policy for active Azure run execution detail.
/// Does not invent progress from order, elapsed time, or percentComplete.
/// </summary>
public static class AzureRunExecutionProjector
{
    public sealed record Presentation(
        string? Summary,
        string? Detail,
        ActivityProgress? Progress,
        string? ProgressCaption);

    public static AzureRunExecutionDetail? TryCreateDetail(long runId, AzureBuildTimelineResult timeline)
    {
        if (timeline.Outcome != AzureBuildTimelineOutcome.Ok || timeline.Records.Count == 0)
        {
            return null;
        }

        var byId = timeline.Records.ToDictionary(r => r.Id);
        var stages = timeline.Records
            .Where(r => IsType(r, "Stage") && !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new AzureTimelineStageInfo(
                r.Id,
                r.Name!.Trim(),
                r.State,
                r.Result,
                r.Order,
                r.StartedAtUtc,
                r.FinishedAtUtc))
            .OrderBy(s => s.Order ?? int.MaxValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var jobs = new List<AzureTimelineJobInfo>();
        foreach (var record in timeline.Records.Where(r => IsType(r, "Job") && !string.IsNullOrWhiteSpace(r.Name)))
        {
            var stage = FindStageParent(record, byId);
            jobs.Add(new AzureTimelineJobInfo(
                record.Id,
                stage?.Id,
                record.Name!.Trim(),
                stage?.Name,
                record.State,
                record.Result,
                record.Order,
                record.StartedAtUtc,
                record.FinishedAtUtc));
        }

        jobs = jobs
            .OrderBy(j => j.Order ?? int.MaxValue)
            .ThenBy(j => j.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (stages.Count == 0 && jobs.Count == 0)
        {
            return null;
        }

        return new AzureRunExecutionDetail(runId, timeline.ChangeId, stages, jobs);
    }

    public static Presentation Present(AzureRunExecutionDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var activeStages = detail.Stages.Where(s => IsInProgress(s.State)).ToList();
        var activeJobs = detail.Jobs.Where(j => IsInProgress(j.State)).ToList();

        string? summary = null;
        string? detailText = null;

        if (activeStages.Count == 1)
        {
            summary = activeStages[0].Name;
            var jobsInStage = activeJobs
                .Where(j => j.StageId == activeStages[0].Id
                            || string.Equals(j.StageName, activeStages[0].Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (jobsInStage.Count == 1)
            {
                detailText = jobsInStage[0].Name;
            }
            else if (jobsInStage.Count > 1)
            {
                detailText = $"{jobsInStage.Count} jobs running";
            }
        }
        else if (activeStages.Count > 1)
        {
            summary = $"{activeStages.Count} stages running";
        }
        else if (activeJobs.Count == 1)
        {
            summary = activeJobs[0].Name;
            detailText = string.IsNullOrWhiteSpace(activeJobs[0].StageName) ? null : activeJobs[0].StageName;
        }
        else if (activeJobs.Count > 1)
        {
            summary = $"{activeJobs.Count} jobs running";
        }

        var progress = TryCreateSequentialStageProgress(detail.Stages, activeStages);
        string? caption = null;
        if (progress is { Total: > 0 } p)
        {
            caption = $"Stage {p.Current} of {p.Total}";
        }

        return new Presentation(summary, detailText, progress, caption);
    }

    /// <summary>
    /// Sequential progress only when ordered stage states form a simple prefix of completed
    /// stages followed by exactly one active stage. Never uses raw Order as current.
    /// </summary>
    public static ActivityProgress? TryCreateSequentialStageProgress(
        IReadOnlyList<AzureTimelineStageInfo> stages,
        IReadOnlyList<AzureTimelineStageInfo>? activeStages = null)
    {
        if (stages.Count == 0)
        {
            return null;
        }

        // Require an order on every stage so the set is a coherent sequence.
        if (stages.Any(s => s.Order is null))
        {
            return null;
        }

        // Duplicate orders are ambiguous — do not guess.
        if (stages.Select(s => s.Order!.Value).Distinct().Count() != stages.Count)
        {
            return null;
        }

        var ordered = stages
            .OrderBy(s => s.Order!.Value)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        activeStages ??= ordered.Where(s => IsInProgress(s.State)).ToList();
        if (activeStages.Count != 1)
        {
            return null;
        }

        var active = activeStages[0];
        var activeIndex = ordered.FindIndex(s => s.Id == active.Id);
        if (activeIndex < 0)
        {
            return null;
        }

        // All stages before the active one must be completed/terminal.
        for (var i = 0; i < activeIndex; i++)
        {
            if (!IsCompleted(ordered[i].State))
            {
                return null;
            }
        }

        // No stage after the active one may already be completed or in progress.
        for (var i = activeIndex + 1; i < ordered.Count; i++)
        {
            if (IsCompleted(ordered[i].State) || IsInProgress(ordered[i].State))
            {
                return null;
            }
        }

        // current = 1-based position in the ordered eligible list — not Order, not completed+1.
        return ActivityProgress.TryCreate(activeIndex + 1, ordered.Count);
    }

    public static bool IsInProgress(string? state) =>
        string.Equals(state?.Trim(), "inProgress", StringComparison.OrdinalIgnoreCase);

    public static bool IsCompleted(string? state) =>
        string.Equals(state?.Trim(), "completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsType(AzureBuildTimelineRecord record, string type) =>
        string.Equals(record.Type, type, StringComparison.OrdinalIgnoreCase);

    private static AzureBuildTimelineRecord? FindStageParent(
        AzureBuildTimelineRecord record,
        IReadOnlyDictionary<Guid, AzureBuildTimelineRecord> byId)
    {
        var current = record;
        var visited = new HashSet<Guid>();
        while (current.ParentId is { } parentId && visited.Add(parentId))
        {
            if (!byId.TryGetValue(parentId, out var parent))
            {
                return null;
            }

            if (IsType(parent, "Stage"))
            {
                return parent;
            }

            current = parent;
        }

        return null;
    }
}
