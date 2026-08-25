using BuildMonitor.Core.Rules;

namespace BuildMonitor.TrayApp;

/// <summary>Hover status panel layout constants (delegates to <see cref="StatusPanelMetrics"/>).</summary>
internal static class StatusPanelLayout
{
    public const double WindowWidth = StatusPanelMetrics.WindowWidth;
    public const double WindowMinWidth = StatusPanelMetrics.WindowMinWidth;
    public const double WindowMaxWidth = StatusPanelMetrics.WindowMaxWidth;
    public const double AccentColumnWidth = StatusPanelMetrics.AccentColumnWidth;
    public const double SideRailWidth = StatusPanelMetrics.SideRailWidth;
    public const double SideRailMargin = StatusPanelMetrics.SideRailMargin;
    public const double BorderPadding = StatusPanelMetrics.BorderPadding;
    public const double HeaderRowHeight = StatusPanelMetrics.HeaderRowHeight;
    public const double HeaderBottomMargin = StatusPanelMetrics.HeaderBottomMargin;
    public const double MinBodyHeight = StatusPanelMetrics.MinBodyHeight;

    public const double ContentMeasureWidth = StatusPanelMetrics.ContentMeasureWidth;

    public static double VerticalChrome => (BorderPadding * 2) + 2 + HeaderRowHeight + HeaderBottomMargin;

    public static double MaxBodyHeight()
    {
        var workArea = System.Windows.SystemParameters.WorkArea;
        return Math.Max(240, workArea.Height * 0.88 - VerticalChrome);
    }
}
