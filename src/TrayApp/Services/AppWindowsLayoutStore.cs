using System.IO;
using System.Text.Json;
using System.Windows;

namespace BuildMonitor.TrayApp.Services;

public sealed class AppWindowsLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string layoutPath;
    private readonly string? legacyBuildLogPath;

    public AppWindowsLayout Layout { get; private set; } = new();

    public AppWindowsLayoutStore(string appDataDirectory)
    {
        layoutPath = Path.Combine(appDataDirectory, "windows-layout.json");
        legacyBuildLogPath = Path.Combine(appDataDirectory, "build-log-window.json");
    }

    public async Task LoadAsync()
    {
        if (File.Exists(layoutPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(layoutPath);
                Layout = JsonSerializer.Deserialize<AppWindowsLayout>(json, JsonOptions) ?? new AppWindowsLayout();
                return;
            }
            catch
            {
                Layout = new AppWindowsLayout();
            }
        }

        await MigrateLegacyBuildLogStateAsync();
    }

    public Task SaveAsync()
    {
        var directory = Path.GetDirectoryName(layoutPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(Layout, JsonOptions);
        return File.WriteAllTextAsync(layoutPath, json);
    }

    private async Task MigrateLegacyBuildLogStateAsync()
    {
        if (legacyBuildLogPath is null || !File.Exists(legacyBuildLogPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(legacyBuildLogPath);
            var legacy = JsonSerializer.Deserialize<BuildLogViewerWindowState>(json, JsonOptions);
            if (legacy is null)
            {
                return;
            }

            Layout.BuildLog = new BuildLogViewerLayoutState
            {
                Left = legacy.Left,
                Top = legacy.Top,
                Width = legacy.Width,
                Height = legacy.Height,
                LogPanelRatio = legacy.LogPanelRatio,
                FollowOutput = legacy.FollowOutput
            };
        }
        catch
        {
            // Best effort migration only.
        }
    }
}
