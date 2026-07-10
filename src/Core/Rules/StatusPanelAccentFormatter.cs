using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class StatusPanelAccentFormatter
{
    public static bool ShouldShowAccentRail(ProjectHealthSnapshot snapshot) =>
        snapshot.IsActive
        && (snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing
            || snapshot.IsRestarting
            || StatusPanelBuildVisibilityEvaluator.ShouldShowSiteAwaiting(snapshot));

    public static string FormatActivityLabel(ProjectHealthSnapshot snapshot)
    {
        if (snapshot.State == ProjectLifecycleState.Testing)
        {
            return "Running tests";
        }

        if (snapshot.State == ProjectLifecycleState.Building)
        {
            var failed = snapshot.ProgressSteps.FirstOrDefault(s => s.Status == BuildStepStatus.Failed);
            if (failed is not null)
            {
                return "Build failed";
            }

            var active = snapshot.ProgressSteps.FirstOrDefault(s => s.Status == BuildStepStatus.Active);
            if (active is not null)
            {
                return FormatActiveBuildStep(active.Label);
            }

            return "Building";
        }

        if (snapshot.IsRestarting)
        {
            return "Launching app";
        }

        if (StatusPanelBuildVisibilityEvaluator.ShouldShowSiteAwaiting(snapshot))
        {
            return "Starting site";
        }

        return "Working";
    }

    public static MonitorHealth ResolveAccentHealth(ProjectHealthSnapshot snapshot)
    {
        if (snapshot.ErrorCount > 0)
        {
            return MonitorHealth.Red;
        }

        if (snapshot.WarningCount > 0)
        {
            return MonitorHealth.Amber;
        }

        return snapshot.Health;
    }

    private static string FormatActiveBuildStep(string label)
    {
        if (label.Contains("restore", StringComparison.OrdinalIgnoreCase))
        {
            return "Restoring";
        }

        if (label.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Build failed";
        }

        var shortName = label.Length > 16 ? label[..14] + "…" : label;
        return $"Compiling {shortName}";
    }
}
