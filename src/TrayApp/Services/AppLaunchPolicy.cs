using BuildMonitor.Core.Settings;

namespace BuildMonitor.TrayApp.Services;

/// <summary>Startup migration and launch-gate helpers extracted from <c>App</c>.</summary>
internal static class AppLaunchPolicy
{
    public static void MigrateLegacyAppDataIfNeeded(string newAppDataDirectory)
    {
        if (Directory.Exists(newAppDataDirectory))
        {
            return;
        }

        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AzureBuildMonitor");
        if (!Directory.Exists(legacyDirectory))
        {
            return;
        }

        try
        {
            Directory.Move(legacyDirectory, newAppDataDirectory);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Legacy app data migration failed: {ex.Message}");
        }
    }

    public static bool ShouldSkipProjectStart()
    {
        var value = Environment.GetEnvironmentVariable("BUILDMONITOR_SKIP_PROJECT_START");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldAutoStartAnyProjectsOnLaunch(AppSettings settings) =>
        !ShouldSkipProjectStart()
        && settings.Projects.Any(p => p.IsActiveInSession && p.StartOnLaunch);

    public static bool ShouldAutoOpenBuildMonitorHealth(AppSettings settings)
    {
        var value = Environment.GetEnvironmentVariable("BUILDMONITOR_AUTO_BUILD_MONITOR_HEALTH");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Environment.GetEnvironmentVariable("BUILDMONITOR_AUTO_THREAD_HEALTH");
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return settings.Monitor.AutoOpenBuildMonitorHealthOnStartup;
    }
}
