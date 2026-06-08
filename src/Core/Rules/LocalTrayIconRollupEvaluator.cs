using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class LocalTrayIconRollupEvaluator
{
    public static MonitorHealth Rollup(IReadOnlyList<ProjectHealthSnapshot> activeProjects)
    {
        if (activeProjects.Count == 0)
        {
            return MonitorHealth.Unknown;
        }

        if (activeProjects.Any(p => p.Health == MonitorHealth.Red))
        {
            return MonitorHealth.Red;
        }

        if (activeProjects.Any(p => p.Health == MonitorHealth.Amber))
        {
            return MonitorHealth.Amber;
        }

        if (activeProjects.All(p => p.Health == MonitorHealth.Green))
        {
            return MonitorHealth.Green;
        }

        return MonitorHealth.Unknown;
    }

    public static bool IsBuilding(IReadOnlyList<ProjectHealthSnapshot> activeProjects) =>
        activeProjects.Any(p => p.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing);

    public static ProjectHealthSnapshot? ChooseHeadline(IReadOnlyList<ProjectHealthSnapshot> activeProjects)
    {
        if (activeProjects.Count == 0)
        {
            return null;
        }

        var busy = activeProjects
            .Where(p => p.State is ProjectLifecycleState.Building
                or ProjectLifecycleState.Running
                or ProjectLifecycleState.Watching
                or ProjectLifecycleState.Testing)
            .OrderByDescending(p => p.LastChangedUtc)
            .FirstOrDefault();

        return busy ?? activeProjects.OrderByDescending(p => p.LastChangedUtc).First();
    }
}
