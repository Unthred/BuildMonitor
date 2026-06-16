using System.Diagnostics;
using System.Text.Json;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class LaunchProfileEnvironmentApplier
{
    public static void ApplyTo(
        ProcessStartInfo startInfo,
        string rootFolder,
        string projectFile,
        string? launchProfile)
    {
        if (string.IsNullOrWhiteSpace(launchProfile))
        {
            return;
        }

        var settings = TryLoadProfile(rootFolder, projectFile, launchProfile);
        if (settings is null)
        {
            return;
        }

        startInfo.Environment.Remove("ASPNETCORE_URLS");
        startInfo.Environment.Remove("ASPNETCORE_HTTPS_PORT");
        startInfo.Environment.Remove("ASPNETCORE_ENVIRONMENT");

        if (!string.IsNullOrWhiteSpace(settings.ApplicationUrl))
        {
            startInfo.Environment["ASPNETCORE_URLS"] = settings.ApplicationUrl;
        }

        foreach (var pair in settings.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }
    }

    public static string? ResolvePrimaryListenUrl(string rootFolder, string projectFile, string? launchProfile)
    {
        var urls = ResolveListenUrls(rootFolder, projectFile, launchProfile);
        return urls.Count > 0 ? urls[0] : null;
    }

    public static string? ResolveEffectiveLaunchProfile(
        string rootFolder,
        string projectFile,
        string? configuredProfile)
    {
        if (!string.IsNullOrWhiteSpace(configuredProfile))
        {
            return configuredProfile;
        }

        var profiles = ListProfileNames(rootFolder, projectFile);
        if (profiles.Count == 0)
        {
            return null;
        }

        var https = profiles.FirstOrDefault(p => p.Equals("https", StringComparison.OrdinalIgnoreCase));
        return https ?? profiles[0];
    }

    public static IReadOnlyList<string> ResolveListenUrls(
        string rootFolder,
        string projectFile,
        string? launchProfile)
    {
        var effectiveProfile = ResolveEffectiveLaunchProfile(rootFolder, projectFile, launchProfile);
        var settings = string.IsNullOrWhiteSpace(effectiveProfile)
            ? null
            : TryLoadProfile(rootFolder, projectFile, effectiveProfile);

        if (settings is null)
        {
            return [];
        }

        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.ApplicationUrl))
        {
            urls.AddRange(SplitUrlList(settings.ApplicationUrl));
        }

        if (settings.EnvironmentVariables.TryGetValue("ASPNETCORE_URLS", out var envUrls)
            && !string.IsNullOrWhiteSpace(envUrls))
        {
            urls.AddRange(SplitUrlList(envUrls));
        }

        return urls
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<string> SplitUrlList(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> ListProfileNames(string rootFolder, string projectFile)
    {
        var projectPath = Path.IsPathRooted(projectFile)
            ? projectFile
            : Path.Combine(rootFolder, projectFile);
        var projectDir = Path.GetDirectoryName(projectPath);
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
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static LaunchProfileSettings? TryLoadProfile(string rootFolder, string projectFile, string launchProfile)
    {
        var projectPath = Path.IsPathRooted(projectFile)
            ? projectFile
            : Path.Combine(rootFolder, projectFile);
        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return null;
        }

        var launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(launchSettingsPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles)
                || !profiles.TryGetProperty(launchProfile, out var profile))
            {
                return null;
            }

            string? applicationUrl = null;
            if (profile.TryGetProperty("applicationUrl", out var urlElement))
            {
                applicationUrl = urlElement.GetString();
            }

            var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (profile.TryGetProperty("environmentVariables", out var envElement)
                && envElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in envElement.EnumerateObject())
                {
                    environmentVariables[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            return new LaunchProfileSettings(applicationUrl, environmentVariables);
        }
        catch
        {
            return null;
        }
    }

    private sealed record LaunchProfileSettings(
        string? ApplicationUrl,
        IReadOnlyDictionary<string, string> EnvironmentVariables);
}
