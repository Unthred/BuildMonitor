using System.Text.Json.Serialization;

namespace BuildMonitor.TrayApp.Services;

public class WindowLayoutState
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = double.NaN;
    public double Height { get; set; } = double.NaN;
    public int WindowState { get; set; }
}

public sealed class BuildLogViewerLayoutState : WindowLayoutState
{
    public double LogPanelRatio { get; set; } = 0.65;
    public bool FollowOutput { get; set; } = true;
}

public sealed class AppWindowsLayout
{
    public BuildLogViewerLayoutState BuildLog { get; set; } = new();
    public WindowLayoutState Settings { get; set; } = new();
    public WindowLayoutState Diagnostics { get; set; } = new();
    [JsonPropertyName("threadHealth")]
    public WindowLayoutState BuildMonitorHealth { get; set; } = new();
    public WindowLayoutState StatusPanel { get; set; } = new() { Width = 480, Height = 420 };
}
