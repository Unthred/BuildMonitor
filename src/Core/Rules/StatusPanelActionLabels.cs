namespace BuildMonitor.Core.Rules;

/// <summary>Status panel action button labels — keep in sync with HoverStatusPanel wiring.</summary>
public static class StatusPanelActionLabels
{
    public const string Rebuild = "Rebuild";
    public const string RebuildToolTip = "Build the project (does not restart a run host)";
    public const string RebuildAndRestart = "Rebuild & restart";
    public const string RebuildAndRestartToolTip = "Full build, then start run/watch";
}
