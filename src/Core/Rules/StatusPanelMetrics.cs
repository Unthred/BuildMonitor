namespace BuildMonitor.Core.Rules;

/// <summary>Hover status panel sizing — single source of truth for XAML and layout fit.</summary>
public static class StatusPanelMetrics
{
    /// <summary>Compact width that still fits Azure Run / Build No. / PR without clipping.</summary>
    public const double WindowWidth = 620;

    public const double WindowMinWidth = 600;
    public const double WindowMaxWidth = 640;

    /// <summary>Side rail collapsed — overall health lives in the action footer.</summary>
    public const double AccentColumnWidth = 0;
    public const double SideRailWidth = 0;
    public const double SideRailMargin = 0;
    public const double BorderPadding = 6;
    public const double HeaderRowHeight = 22;
    public const double HeaderBottomMargin = 2;
    public const double MinBodyHeight = 64;

    public const double ContentMeasureWidth =
        WindowWidth - (BorderPadding * 2) - 2;
}
