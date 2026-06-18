using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class CorruptedOutputTreeDetector
{
    private static readonly Regex NestedArtifactsPathRegex = new(
        @"artifacts[\\/][^""'\r\n]*[\\/]bin[\\/][^""'\r\n]*artifacts[\\/]build",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] CorruptedLogMarkers =
    [
        "Could not find file",
        "Could not copy",
        "error MSB3030",
        "error MSB3027",
        "error MSB3021",
        "FileNotFoundException"
    ];

    public static bool HasRiskyBaseOutputPath(string? extraDotNetArgs) =>
        !string.IsNullOrWhiteSpace(extraDotNetArgs)
        && extraDotNetArgs.Contains("BaseOutputPath", StringComparison.OrdinalIgnoreCase);

    public static bool IsCorruptedTreeFailure(string? logText, string? projectRoot = null)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return HasNestedArtifactsOnDisk(projectRoot);
        }

        if (NestedArtifactsPathRegex.IsMatch(logText))
        {
            return true;
        }

        if (ContainsCorruptedArtifactsCopyFailure(logText))
        {
            return true;
        }

        return HasNestedArtifactsOnDisk(projectRoot);
    }

    public static bool HasNestedArtifactsOnDisk(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return false;
        }

        var artifactsBuild = Path.Combine(projectRoot, "artifacts", "build");
        if (!Directory.Exists(artifactsBuild))
        {
            return false;
        }

        try
        {
            foreach (var nestedArtifacts in Directory.EnumerateDirectories(
                         artifactsBuild,
                         "artifacts",
                         SearchOption.AllDirectories))
            {
                if (Directory.Exists(Path.Combine(nestedArtifacts, "build")))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool ContainsCorruptedArtifactsCopyFailure(string logText)
    {
        if (!logText.Contains("artifacts", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CorruptedLogMarkers.Any(marker =>
            logText.Contains(marker, StringComparison.OrdinalIgnoreCase)
            && (logText.Contains(@"artifacts\build", StringComparison.OrdinalIgnoreCase)
                || logText.Contains("artifacts/build", StringComparison.OrdinalIgnoreCase)));
    }
}
