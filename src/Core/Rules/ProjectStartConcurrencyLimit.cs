namespace BuildMonitor.Core.Rules;

/// <summary>
/// Applies <c>Monitor.MaxConcurrentActiveProjects</c> to automatic starts (launch, derived-worktree
/// register, newly activated project on Settings Save). Projects held back are not silent: they carry
/// <see cref="FormatNotStartedReason"/> on their card and in <c>GET /projects</c>.
/// A user's explicit Start ignores the limit.
/// </summary>
public static class ProjectStartConcurrencyLimit
{
    public sealed record Partition<T>(IReadOnlyList<T> Start, IReadOnlyList<T> Deferred);

    /// <summary>
    /// Splits <paramref name="candidates"/> (in settings order) into those that fit beside
    /// <paramref name="alreadyStarted"/> running projects and those held back by <paramref name="limit"/>.
    /// </summary>
    public static Partition<T> Split<T>(IReadOnlyList<T> candidates, int alreadyStarted, int limit)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var free = Math.Max(0, limit - Math.Max(0, alreadyStarted));
        var take = Math.Min(free, candidates.Count);
        return new Partition<T>(
            candidates.Take(take).ToList(),
            candidates.Skip(take).ToList());
    }

    public static bool HasFreeSlot(int alreadyStarted, int limit) => alreadyStarted < limit;

    public static string FormatNotStartedReason(int limit) =>
        limit == 1
            ? "Not started: limit of 1 active project reached"
            : $"Not started: limit of {limit} active projects reached";

    public static string FormatNotificationTitle(int deferredCount) =>
        deferredCount == 1
            ? "1 project not started"
            : $"{deferredCount} projects not started";

    public static string FormatNotificationMessage(IReadOnlyList<string> displayNames, int limit)
    {
        ArgumentNullException.ThrowIfNull(displayNames);
        var names = string.Join(", ", displayNames);
        return $"{names}: limit of {limit} active projects reached. "
            + "Use Start on the card, or raise Max concurrent active projects in Settings.";
    }
}
