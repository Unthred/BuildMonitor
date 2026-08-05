namespace BuildMonitor.Infrastructure.LocalBuild;

public static class DotNetBuildArguments
{
    /// <summary>
    /// Startup / explicit Rebuild always force a non-incremental compile so counts are complete.
    /// </summary>
    public static bool RequiresFullRebuild(string? buildReason) =>
        string.Equals(buildReason, "startup", StringComparison.OrdinalIgnoreCase)
        || string.Equals(buildReason, "manual rebuild", StringComparison.OrdinalIgnoreCase)
        || string.Equals(buildReason, "rebuild & restart", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// When <paramref name="forceCompleteWarningCounts"/> is true, every build uses
    /// <c>--no-incremental</c>. Otherwise only startup / Rebuild / Rebuild &amp; restart do.
    /// </summary>
    public static bool ShouldForceFullRebuild(string? buildReason, bool forceCompleteWarningCounts) =>
        forceCompleteWarningCounts || RequiresFullRebuild(buildReason);

    public static void ApplyFullRebuildFlag(IList<string> arguments, bool forceFullRebuild)
    {
        if (forceFullRebuild && !arguments.Contains("--no-incremental", StringComparer.OrdinalIgnoreCase))
        {
            arguments.Add("--no-incremental");
        }
    }
}
