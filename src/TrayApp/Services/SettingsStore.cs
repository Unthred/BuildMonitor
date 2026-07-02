using System.IO;
using System.Text.Json;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.TrayApp.Services;

public sealed class SettingsStore(string settingsPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<AppSettings> LoadOrCreateDefaultAsync()
    {
        if (!File.Exists(settingsPath))
        {
            var defaults = BuildDefaults();
            await SaveAsync(defaults);
            return defaults;
        }

        var json = await File.ReadAllTextAsync(settingsPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        if (settings is null)
        {
            return BuildDefaults();
        }

        if (settings.SchemaVersion < 2)
        {
            settings.SchemaVersion = 2;
        }

        if (settings.SchemaVersion < 3)
        {
            settings.Monitor.CoalesceWatchRebuilds = true;
            settings.SchemaVersion = 3;
        }

        if (settings.SchemaVersion < 4)
        {
            foreach (var project in settings.Projects)
            {
                project.RunOptions.AutoRestartOnHotReloadRequest = true;
            }

            settings.SchemaVersion = 4;
        }

        if (settings.SchemaVersion < 5)
        {
            foreach (var project in settings.Projects)
            {
                project.RunOptions.AutoRepairCorruptedOutput = true;
            }

            settings.AppBehavior.TrayMenuLayout = TrayMenuLayout.ByOperation;
            settings.SchemaVersion = 5;
        }

        if (settings.SchemaVersion < 6)
        {
            settings.Monitor.FileChangeDebounceMode = FileChangeDebounceMode.Manual;
            settings.SchemaVersion = 6;
        }

        if (settings.SchemaVersion < 7)
        {
            settings.Monitor.AutoOpenBuildMonitorHealthOnStartup = true;
            settings.SchemaVersion = 7;
        }

        if (settings.SchemaVersion < 8)
        {
            MigrateAutoOpenBuildMonitorHealth(json, settings);
            settings.SchemaVersion = 8;
        }

        if (settings.SchemaVersion < 9)
        {
            settings.SchemaVersion = 9;
        }

        if (settings.SchemaVersion < 10)
        {
            MigrateStartOnLaunch(json, settings);
            settings.SchemaVersion = 10;
        }

        if (settings.SchemaVersion < 11)
        {
            MigrateAutoOpenLog(json, settings);
            settings.SchemaVersion = 11;
        }

        if (settings.SchemaVersion < 12)
        {
            settings.SchemaVersion = 12;
        }

        if (settings.SchemaVersion < 13)
        {
            settings.SchemaVersion = 13;
        }

        return settings;
    }

    private static void MigrateAutoOpenLog(string json, AppSettings settings)
    {
        var legacyErrorsOnly = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("monitor", out var monitor)
                && monitor.TryGetProperty("autoOpenLogOnFailure", out var legacy))
            {
                legacyErrorsOnly = legacy.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // keep default false
        }

        var migratedMode = legacyErrorsOnly ? AutoOpenLogMode.Errors : AutoOpenLogMode.Never;
        foreach (var project in settings.Projects)
        {
            project.RunOptions.AutoOpenLog = migratedMode;
        }
    }

    private static void MigrateStartOnLaunch(string json, AppSettings settings)
    {
        var autoStart = true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("monitor", out var monitor)
                && monitor.TryGetProperty("autoStartActiveProjectsOnLaunch", out var legacy))
            {
                autoStart = legacy.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // keep default true
        }

        foreach (var project in settings.Projects)
        {
            project.StartOnLaunch = autoStart;
        }
    }

    private static void MigrateAutoOpenBuildMonitorHealth(string json, AppSettings settings)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("monitor", out var monitor))
            {
                return;
            }

            if (monitor.TryGetProperty("autoOpenBuildMonitorHealthOnStartup", out _))
            {
                return;
            }

            if (monitor.TryGetProperty("autoOpenThreadHealthOnStartup", out var legacy))
            {
                settings.Monitor.AutoOpenBuildMonitorHealthOnStartup = legacy.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // keep deserialized defaults
        }
    }

    public Task SaveAsync(AppSettings settings)
    {
        settings.SchemaVersion = 13;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return File.WriteAllTextAsync(settingsPath, json);
    }

    private static AppSettings BuildDefaults() => new();
}
