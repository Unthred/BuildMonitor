namespace BuildMonitor.Core.Rules;

using BuildMonitor.Core.Models;

public sealed record BuildSuppressionSettings(
    bool DeferStartupBuildUntilQuiet,
    bool CancelSupersededBuilds);

public static class BuildSuppressionPolicy
{
    public static bool ShouldDeferStartupBuild(
        BuildSuppressionSettings settings,
        EditActivitySnapshot activity) =>
        settings.DeferStartupBuildUntilQuiet && activity.IsActive;

    public static bool ShouldCancelInFlightBuild(
        BuildSuppressionSettings settings,
        string? buildReason)
    {
        if (!settings.CancelSupersededBuilds || string.IsNullOrWhiteSpace(buildReason))
        {
            return false;
        }

        if (buildReason.Contains("(lock retry)", StringComparison.OrdinalIgnoreCase)
            || buildReason.Contains("(output repair retry)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsStartupReason(buildReason)
            || IsFileChangeReason(buildReason);
    }

    public static bool IsEditGatingActive(
        BuildSuppressionSettings settings,
        bool pendingFileChangeRebuild,
        EditActivitySnapshot activity,
        PendingRebuildHoldReason holdReason) =>
        (settings.DeferStartupBuildUntilQuiet || settings.CancelSupersededBuilds)
        && (pendingFileChangeRebuild
            || holdReason is PendingRebuildHoldReason.StartupDeferred
                or PendingRebuildHoldReason.SupersededByNewEdits
            || activity.IsActive);

    private static bool IsStartupReason(string buildReason) =>
        string.Equals(buildReason, "startup", StringComparison.OrdinalIgnoreCase)
        || buildReason.StartsWith("startup ", StringComparison.OrdinalIgnoreCase);

    private static bool IsFileChangeReason(string buildReason) =>
        string.Equals(buildReason, "file change", StringComparison.OrdinalIgnoreCase)
        || string.Equals(buildReason, "file change (queued)", StringComparison.OrdinalIgnoreCase);
}
