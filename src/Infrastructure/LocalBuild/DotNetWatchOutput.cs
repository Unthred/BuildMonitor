using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class DotNetWatchOutput
{
    private static readonly Regex MsBuildFailedLineRegex = new(
        @"^\s*Build FAILED\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// dotnet watch build summary after a watch-triggered compile, e.g.
    /// "dotnet watch ❌ App.csproj failed with 2 error(s) (1.2s)".
    /// </summary>
    private static readonly Regex DotNetWatchCompileFailedRegex = new(
        @"dotnet watch.*failed with \d+ error",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DotNetWatchClassicCompileFailedRegex = new(
        @"The build failed\. Please fix the build errors",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
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

    /// <summary>
    /// Detects MSBuild / dotnet watch compile failures. Avoids matching arbitrary app log text
    /// such as "The build failed validation" from runtime stdout.
    /// </summary>
    public static bool IsBuildFailedLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = StripAnsi(line).Trim();
        if (MsBuildFailedLineRegex.IsMatch(trimmed))
        {
            return true;
        }

        // Hot reload / browser refresh messages also contain "failed" but are not compile failures.
        if (IsHotReloadOrRuntimeFailureLine(trimmed))
        {
            return false;
        }

        return DotNetWatchCompileFailedRegex.IsMatch(trimmed)
               || DotNetWatchClassicCompileFailedRegex.IsMatch(trimmed);
    }

    private static bool IsHotReloadOrRuntimeFailureLine(string trimmed) =>
        trimmed.Contains("Change failed to apply", StringComparison.OrdinalIgnoreCase)
        || trimmed.Contains("Previous changes failed to apply", StringComparison.OrdinalIgnoreCase)
        || trimmed.Contains("Failed to receive response from a connected browser", StringComparison.OrdinalIgnoreCase);

    private static string StripAnsi(string line) =>
        Regex.Replace(line, @"\x1b\[[0-9;]*m", string.Empty);

    public static bool IsBuildSucceededLine(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && (line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Build succeeded.", StringComparison.OrdinalIgnoreCase));
}
