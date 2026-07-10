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
        webReady && health == MonitorHealth.Green
            ? "Site up"
            : health switch
            {
                MonitorHealth.Red => "Needs fix",
                MonitorHealth.Amber => "Warnings",
                MonitorHealth.Green => "Healthy",
                _ => "Monitoring"
            };
}
