using System.IO;
using System.Text.Json;
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

        return settings;
    }

    public Task SaveAsync(AppSettings settings)
    {
        settings.SchemaVersion = 2;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return File.WriteAllTextAsync(settingsPath, json);
    }

    private static AppSettings BuildDefaults() => new();
}
