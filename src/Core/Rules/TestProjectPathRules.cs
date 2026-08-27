namespace BuildMonitor.Core.Rules;

/// <summary>
/// Validates that a persisted Test project / solution path belongs to the project's root folder.
/// Prevents cross-project relative paths (e.g. Witherby tests under a BuildMonitor root) from sticking.
/// </summary>
public static class TestProjectPathRules
{
    /// <summary>
    /// Empty is valid (auto-discover). Otherwise the path must resolve under <paramref name="rootFolder"/>
    /// and exist as a file when the root exists.
    /// </summary>
    public static bool IsValidForRoot(string? rootFolder, string? testProjectFile)
    {
        if (string.IsNullOrWhiteSpace(testProjectFile))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.IsPathRooted(testProjectFile)
                ? Path.GetFullPath(testProjectFile)
                : Path.GetFullPath(Path.Combine(rootFolder, testProjectFile));
            var rootFull = Path.GetFullPath(rootFolder);
            var rootPrefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        return File.Exists(full);
    }

    /// <summary>
    /// Returns <paramref name="testProjectFile"/> when valid; otherwise empty (auto-discover).
    /// </summary>
    public static string SanitizeForRoot(string? rootFolder, string? testProjectFile) =>
        IsValidForRoot(rootFolder, testProjectFile)
            ? (testProjectFile ?? string.Empty).Trim()
            : string.Empty;
}
