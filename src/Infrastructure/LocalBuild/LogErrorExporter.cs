using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class LogErrorExporter
{
    public static BuildLogKind ResolvePrimaryLogKind(ProjectLifecycleState state, string? issueCountsText) =>
        state switch
        {
            ProjectLifecycleState.Crashed => BuildLogKind.Run,
            ProjectLifecycleState.Running or ProjectLifecycleState.Watching
                when issueCountsText?.StartsWith("Run:", StringComparison.Ordinal) == true => BuildLogKind.Run,
            ProjectLifecycleState.Testing or ProjectLifecycleState.TestFailed => BuildLogKind.Test,
            _ => BuildLogKind.Build
        };

    public static IReadOnlyList<string> GetErrorLines(BuildLogKind kind, string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return [];
        }

        return kind switch
        {
            BuildLogKind.Run => DotNetRunOutputParser.ParseIssues(logText)
                .Where(i => i.IsError)
                .Select(i => i.Text)
                .ToList(),
            BuildLogKind.Test => DotNetTestOutputParser.ParseIssues(logText)
                .Where(i => i.IsError)
                .Select(i => i.Text)
                .ToList(),
            _ => BuildLogParser.ParseErrors(logText).ErrorLines
        };
    }
}
