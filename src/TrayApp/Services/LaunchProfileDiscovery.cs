using System.IO;
using System.Text.Json;

namespace BuildMonitor.TrayApp.Services;

public static class LaunchProfileDiscovery
{
    public static IReadOnlyList<string> DiscoverProfiles(string rootFolder, string projectFile)
    {
        var fullProjectPath = ResolveProjectPath(rootFolder, projectFile);
        if (string.IsNullOrWhiteSpace(fullProjectPath) || !File.Exists(fullProjectPath))
        {
            return [];
        }

        var projectDir = Path.GetDirectoryName(fullProjectPath);
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return [];
        }

        var launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(launchSettingsPath))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles))
            {
                return [];
            }

            return profiles.EnumerateObject()
                .Select(p => p.Name)
                .OrderBy(n => n, ProfileNameComparer.Instance)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static string? ResolveProjectPath(string rootFolder, string projectFile)
    {
        if (string.IsNullOrWhiteSpace(projectFile))
        {
            return null;
        }

        return Path.IsPathRooted(projectFile)
            ? projectFile
            : Path.Combine(rootFolder, projectFile);
    }

    public static string? GetPreferredProfile(IReadOnlyList<string> profiles)
    {
        if (profiles.Count == 0)
        {
            return null;
        }

        var https = profiles.FirstOrDefault(p => p.Equals("https", StringComparison.OrdinalIgnoreCase));
        if (https is not null)
        {
            return https;
        }

        return profiles[0];
    }

    /// <summary>
    /// True when any launch profile declares <c>applicationUrl</c> (web/site-ready projects).
    /// </summary>
    public static bool AnyProfileHasApplicationUrl(string rootFolder, string projectFile)
    {
        var fullProjectPath = ResolveProjectPath(rootFolder, projectFile);
        if (string.IsNullOrWhiteSpace(fullProjectPath) || !File.Exists(fullProjectPath))
        {
            return false;
        }

        var projectDir = Path.GetDirectoryName(fullProjectPath);
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return false;
        }

        var launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(launchSettingsPath))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles))
            {
                return false;
            }

            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.TryGetProperty("applicationUrl", out var url)
                    && !string.IsNullOrWhiteSpace(url.GetString()))
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

    public static string ToRelativePath(string rootFolder, string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return absolutePath;
        }

        try
        {
            var relative = Path.GetRelativePath(rootFolder, absolutePath);
            return relative.StartsWith("..", StringComparison.Ordinal) ? absolutePath : relative;
        }
        catch
        {
            return absolutePath;
        }
    }

    private sealed class ProfileNameComparer : IComparer<string>
    {
        public static ProfileNameComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var rankX = GetRank(x);
            var rankY = GetRank(y);
            if (rankX != rankY)
            {
                return rankX.CompareTo(rankY);
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }

        private static int GetRank(string name)
        {
            if (name.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }
    }
}
