using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class StatusPanelIdleRailFormatter
{
    public static MonitorHealth ResolveHealth(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        var active = snapshots.Where(s => s.IsActive).ToList();
        return LocalTrayIconRollupEvaluator.Rollup(active);
    }

    public static bool ResolveWebReady(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        var active = snapshots.Where(s => s.IsActive).ToList();
        var headline = LocalTrayIconRollupEvaluator.ChooseHeadline(active);
        return LocalTrayIconRollupEvaluator.IsWebReady(headline);
    }

    public static string FormatIdleLabel(MonitorHealth health, bool webReady) =>
        StatusPanelOverallFormatter.FormatLabelFromHealth(health, webReady);
}
