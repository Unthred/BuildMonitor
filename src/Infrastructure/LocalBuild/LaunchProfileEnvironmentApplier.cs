using System.Diagnostics;
using System.Text.Json;

namespace BuildMonitor.Infrastructure.LocalBuild;

public readonly record struct LaunchProfileApplyResult(
    string? LaunchProfile,
    string? EnvironmentName,
    string? EffectiveUrls,
    string LaunchSettingsPath);

/// <summary>
/// Copies the selected launch profile's environment into a child ProcessStartInfo only.
/// Never writes launchSettings.json. Never mutates BuildMonitor's process environment.
/// Precedence for listen URLs:
/// 1. BuildMonitor effective URL (<c>local.applicationUrl</c> / port isolation) as <c>ASPNETCORE_URLS</c>
/// 2. Profile <c>environmentVariables.ASPNETCORE_URLS</c>
/// 3. Profile <c>applicationUrl</c>
/// </summary>
public static class LaunchProfileEnvironmentApplier
{
    public static LaunchProfileApplyResult ApplyTo(
        ProcessStartInfo startInfo,
        string rootFolder,
        string projectFile,
        string? launchProfile,
        string? applicationUrlOverride = null)
    {
        var launchSettingsPath = ResolveLaunchSettingsPath(rootFolder, projectFile);
        var effectiveProfile = ResolveEffectiveLaunchProfile(rootFolder, projectFile, launchProfile);

        startInfo.Environment.Remove("ASPNETCORE_URLS");
        startInfo.Environment.Remove("ASPNETCORE_HTTPS_PORT");
        startInfo.Environment.Remove("DOTNET_LAUNCH_PROFILE");

        var settings = string.IsNullOrWhiteSpace(effectiveProfile)
            ? null
            : TryLoadProfile(rootFolder, projectFile, effectiveProfile);

        if (settings is not null)
        {
            startInfo.Environment.Remove("ASPNETCORE_ENVIRONMENT");

            foreach (var pair in settings.EnvironmentVariables)
            {
                if (pair.Key.Equals("ASPNETCORE_URLS", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(applicationUrlOverride))
                {
                    continue;
                }

                startInfo.Environment[pair.Key] = pair.Value;
            }

            if (string.IsNullOrWhiteSpace(applicationUrlOverride)
                && !HasEnvironmentValue(startInfo, "ASPNETCORE_URLS")
                && !string.IsNullOrWhiteSpace(settings.ApplicationUrl))
            {
                startInfo.Environment["ASPNETCORE_URLS"] = settings.ApplicationUrl;
            }
        }

        if (!string.IsNullOrWhiteSpace(applicationUrlOverride))
        {
            startInfo.Environment["ASPNETCORE_URLS"] = applicationUrlOverride;
        }

        return new LaunchProfileApplyResult(
            effectiveProfile,
            GetEnvironmentValue(startInfo, "ASPNETCORE_ENVIRONMENT"),
            GetEnvironmentValue(startInfo, "ASPNETCORE_URLS"),
            launchSettingsPath);
    }

    public static string FormatStartDiagnostics(
        string displayName,
        string rootFolder,
        string startupProjectPath,
        string? launchProfile,
        string? environmentName,
        string? effectiveUrl)
    {
        return $"Starting '{displayName}' root={rootFolder} startup={startupProjectPath} profile={NullDash(launchProfile)} environment={NullDash(environmentName)} url={NullDash(effectiveUrl)}";
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
            .OrderBy(u => LocalPortProbe.GetProfileUrlPriorityRank(u))
            .ThenBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ResolveLaunchSettingsPath(string rootFolder, string projectFile)
    {
        var projectDir = ResolveProjectDirectory(rootFolder, projectFile);
        return Path.Combine(projectDir, "Properties", "launchSettings.json");
    }

    public static string ResolveProjectDirectory(string rootFolder, string projectFile)
    {
        var projectPath = Path.IsPathRooted(projectFile)
            ? projectFile
            : Path.Combine(rootFolder, projectFile);
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        return string.IsNullOrWhiteSpace(projectDir) ? Path.GetFullPath(rootFolder) : projectDir;
    }

    private static string NullDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();

    private static bool HasEnvironmentValue(ProcessStartInfo startInfo, string key) =>
        startInfo.Environment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static string? GetEnvironmentValue(ProcessStartInfo startInfo, string key) =>
        startInfo.Environment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static IReadOnlyList<string> SplitUrlList(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> ListProfileNames(string rootFolder, string projectFile)
    {
        var launchSettingsPath = ResolveLaunchSettingsPath(rootFolder, projectFile);
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
        var launchSettingsPath = ResolveLaunchSettingsPath(rootFolder, projectFile);
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
