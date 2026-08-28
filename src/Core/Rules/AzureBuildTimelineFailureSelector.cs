using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Selects the best failed timeline record for Azure <c>view=logs</c> deep links.</summary>
public static class AzureBuildTimelineFailureSelector
{
    private static readonly HashSet<string> FailureResults = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "partiallysucceeded",
        "canceled"
    };

    public sealed record FailedLogTarget(Guid JobId, Guid? TaskId);

    public static FailedLogTarget? TrySelectBestFailedLogTarget(IReadOnlyList<AzureBuildTimelineRecord> records)
    {
        if (records.Count == 0)
        {
            return null;
        }

        var byId = records.ToDictionary(r => r.Id);
        var failedTasks = records
            .Where(r => IsType(r, "Task") && IsFailureResult(r.Result))
            .ToList();
        if (failedTasks.Count > 0)
        {
            var task = failedTasks[^1];
            var jobId = FindJobParentId(task, byId);
            if (jobId is not null)
            {
                return new FailedLogTarget(jobId.Value, task.Id);
            }
        }

        var failedJobs = records
            .Where(r => IsType(r, "Job") && IsFailureResult(r.Result))
            .ToList();
        if (failedJobs.Count > 0)
        {
            return new FailedLogTarget(failedJobs[^1].Id, null);
        }

        var failedStages = records
            .Where(r => IsType(r, "Stage") && IsFailureResult(r.Result))
            .ToList();
        if (failedStages.Count > 0)
        {
            return new FailedLogTarget(failedStages[^1].Id, null);
        }

        return null;
    }

    private static Guid? FindJobParentId(
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

            if (IsType(parent, "Job"))
            {
                return parent.Id;
            }

            current = parent;
        }

        return null;
    }

    private static bool IsType(AzureBuildTimelineRecord record, string type) =>
        string.Equals(record.Type, type, StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureResult(string? result) =>
        !string.IsNullOrWhiteSpace(result) && FailureResults.Contains(result.Trim());
}
