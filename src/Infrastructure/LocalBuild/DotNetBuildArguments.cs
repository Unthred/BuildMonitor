namespace BuildMonitor.Infrastructure.LocalBuild;

public static class DotNetBuildArguments
{
    public static bool RequiresFullRebuild(string? buildReason) =>
        string.Equals(buildReason, "startup", StringComparison.OrdinalIgnoreCase)
        || string.Equals(buildReason, "manual rebuild", StringComparison.OrdinalIgnoreCase)
        || string.Equals(buildReason, "rebuild & restart", StringComparison.OrdinalIgnoreCase);

    public static void ApplyFullRebuildFlag(IList<string> arguments, bool forceFullRebuild)
    {
        if (forceFullRebuild && !arguments.Contains("--no-incremental", StringComparer.OrdinalIgnoreCase))
        {
            arguments.Add("--no-incremental");
        }
    }
}
