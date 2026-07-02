using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class ProjectHealthEvaluator
{
    public static MonitorHealth Evaluate(
        ProjectLifecycleState state,
        int lastBuildExitCode,
        int errorCount,
        int warningCount,
        bool inProgress = false)
    {
        if (inProgress
            || state is ProjectLifecycleState.Building
                or ProjectLifecycleState.Testing
                or ProjectLifecycleState.WaitingForEdits)
        {
            if (errorCount > 0)
            {
                return MonitorHealth.Red;
            }

            if (warningCount > 0)
            {
                return MonitorHealth.Amber;
            }

            return MonitorHealth.Green;
        }

        if (state is ProjectLifecycleState.Idle && lastBuildExitCode < 0)
        {
            return MonitorHealth.Unknown;
        }

        if (state is ProjectLifecycleState.BuildFailed
            or ProjectLifecycleState.Crashed
            or ProjectLifecycleState.TestFailed)
        {
            return MonitorHealth.Red;
        }

        if (state is ProjectLifecycleState.Running or ProjectLifecycleState.Watching)
        {
            if (errorCount > 0)
            {
                return MonitorHealth.Red;
            }

            if (warningCount > 0)
            {
                return MonitorHealth.Amber;
            }

            return MonitorHealth.Green;
        }

        if (lastBuildExitCode >= 0 && lastBuildExitCode != 0)
        {
            return MonitorHealth.Red;
        }

        if (errorCount > 0)
        {
            return MonitorHealth.Red;
        }

        if (warningCount > 0)
        {
            return MonitorHealth.Amber;
        }

        return MonitorHealth.Green;
    }

    public static string ToLabel(MonitorHealth health) =>
        health switch
        {
            MonitorHealth.Green => "Success",
            MonitorHealth.Amber => "Warnings",
            MonitorHealth.Red => "Failed",
            _ => "Unknown"
        };
}
