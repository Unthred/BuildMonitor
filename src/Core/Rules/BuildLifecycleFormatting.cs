using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class BuildLifecycleFormatting
{
    public static bool IsSuccessfulBuildEndState(ProjectLifecycleState state) =>
        state is ProjectLifecycleState.BuildOk
            or ProjectLifecycleState.Watching
            or ProjectLifecycleState.Running;

    public static string FormatBuildDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"h\:mm\:ss");
        }

        return duration.TotalMinutes >= 1
            ? duration.ToString(@"m\:ss")
            : $"{duration.TotalSeconds:F1}s";
    }
}
