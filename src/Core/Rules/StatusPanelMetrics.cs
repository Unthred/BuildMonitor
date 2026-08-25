namespace BuildMonitor.Core.Rules;

/// <summary>Hover status panel sizing — single source of truth for XAML and layout fit.</summary>
public static class StatusPanelMetrics
{
    public const double WindowWidth = 760;
    public const double WindowMaxWidth = 800;
    public const double AccentColumnWidth = 68;
    public const double SideRailWidth = 60;
    public const double SideRailMargin = 6;
    public const double BorderPadding = 6;
    public const double HeaderRowHeight = 24;
    public const double HeaderBottomMargin = 2;
    public const double MinBodyHeight = 72;

    public const double ContentMeasureWidth =
        WindowWidth - (BorderPadding * 2) - 2 - AccentColumnWidth - SideRailMargin;
}
