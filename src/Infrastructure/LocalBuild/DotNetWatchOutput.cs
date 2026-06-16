namespace BuildMonitor.Infrastructure.LocalBuild;

public static class DotNetWatchOutput
{
    public static bool IsFileChangeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Contains("File changed:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("File updated:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("File added:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Changes detected", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return line.Contains("dotnet watch", StringComparison.OrdinalIgnoreCase)
               && line.Contains("File", StringComparison.OrdinalIgnoreCase)
               && (line.Contains("changed", StringComparison.OrdinalIgnoreCase)
                   || line.Contains("updated", StringComparison.OrdinalIgnoreCase)
                   || line.Contains("added", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsBuildFailedLine(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && (line.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
            || line.Contains("The build failed", StringComparison.OrdinalIgnoreCase));

    public static bool IsBuildSucceededLine(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && (line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Build succeeded.", StringComparison.OrdinalIgnoreCase));

    public static bool IsWatchBuildingLine(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && line.Contains("dotnet watch", StringComparison.OrdinalIgnoreCase)
        && line.Contains("Building", StringComparison.OrdinalIgnoreCase);
}
