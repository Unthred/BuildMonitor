using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>
/// Tracks live test completion from VSTest console result lines already recognized by
/// <see cref="DotNetTestOutputParser"/>. Does not invent a discovery total.
/// </summary>
public sealed class DotNetTestLiveProgressTracker
{
    private readonly object sync = new();
    private int completed;
    private int? total;
    private DateTimeOffset startedAtUtc;

    public void Reset(DateTimeOffset startedAtUtc)
    {
        lock (sync)
        {
            completed = 0;
            total = null;
            this.startedAtUtc = startedAtUtc;
        }
    }

    public TestRunLiveProgress ToSnapshot()
    {
        lock (sync)
        {
            return new TestRunLiveProgress(completed, startedAtUtc, total);
        }
    }

    /// <summary>Returns true when aggregate counters changed.</summary>
    public bool OnOutputLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        lock (sync)
        {
            if (DotNetTestOutputParser.TryParseSummaryLine(rawLine, out var summary)
                && summary.Total > 0)
            {
                var nextCompleted = summary.Passed + summary.Failed + summary.Skipped;
                var changed = completed != nextCompleted || total != summary.Total;
                completed = nextCompleted;
                total = summary.Total;
                return changed;
            }

            if (!DotNetTestOutputParser.TryParseConsoleResultLine(rawLine, out _))
            {
                return false;
            }

            // Prefer summary aggregates when present; do not double-count result lines after summary.
            if (total is not null)
            {
                return false;
            }

            completed++;
            return true;
        }
    }
}
