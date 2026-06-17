using System.IO;
using System.Text.Json;

namespace BuildMonitor.TrayApp.Services;

public sealed class BuildLogViewerWindowState
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 960;
    public double Height { get; set; } = 720;
    public double LogPanelRatio { get; set; } = 0.65;
    public bool FollowOutput { get; set; } = true;
}

public sealed class BuildLogViewerWindowStateStore(string statePath)
{
    private static readonly JsonSerializerOptions JsonOptions = LayoutJsonSerializerOptions.Create();

    public async Task<BuildLogViewerWindowState> LoadOrDefaultAsync()
    {
        if (!File.Exists(statePath))
        {
            return new BuildLogViewerWindowState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(statePath);
            return JsonSerializer.Deserialize<BuildLogViewerWindowState>(json, JsonOptions)
                   ?? new BuildLogViewerWindowState();
        }
        catch
        {
            return new BuildLogViewerWindowState();
        }
    }

    public Task SaveAsync(BuildLogViewerWindowState state)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        return File.WriteAllTextAsync(statePath, json);
    }
}
