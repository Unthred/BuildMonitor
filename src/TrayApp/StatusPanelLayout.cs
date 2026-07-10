namespace BuildMonitor.TrayApp;

/// <summary>Hover status panel layout constants.</summary>
internal static class StatusPanelLayout
{
    public const double WindowWidth = 376;
    public const double AccentColumnWidth = 68;
    public const double SideRailWidth = 60;
    public const double SideRailMargin = 6;
    public const double BorderPadding = 6;
    public const double HeaderRowHeight = 24;
    public const double HeaderBottomMargin = 2;
    public const double MinBodyHeight = 72;

    public const double ContentMeasureWidth =
        WindowWidth - (BorderPadding * 2) - 2 - AccentColumnWidth - SideRailMargin;

    public static double VerticalChrome => (BorderPadding * 2) + 2 + HeaderRowHeight + HeaderBottomMargin;

    public static double MaxBodyHeight()
    {
        var workArea = System.Windows.SystemParameters.WorkArea;
        return Math.Max(240, workArea.Height * 0.88 - VerticalChrome);
    }
}
