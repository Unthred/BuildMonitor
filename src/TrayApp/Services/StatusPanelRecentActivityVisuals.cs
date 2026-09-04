using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BuildMonitor.Core.Models;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace BuildMonitor.TrayApp.Services;

/// <summary>Builds the compact Recent activity expander for hover status cards (#116).</summary>
internal static class StatusPanelRecentActivityVisuals
{
    /// <summary>Caps expanded history height so the status panel stays usable; excess rows scroll.</summary>
    private const double MaxExpandedHistoryHeight = 140;

    public static UIElement Build(
        OperationalHistorySectionPresentation section,
        string projectId,
        ThemePalette palette,
        IDictionary<string, bool> expandedByProject)
    {
        var expander = new Expander
        {
            Header = section.HeaderLabel,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(0),
            IsExpanded = ResolveExpanded(section, projectId, expandedByProject),
            Tag = projectId
        };

        expander.Expanded += (_, _) => expandedByProject[projectId] = true;
        expander.Collapsed += (_, _) => expandedByProject[projectId] = false;

        expander.Content = section.Availability switch
        {
            OperationalHistoryAvailability.Unavailable => MessageBlock(section.UnavailableMessage, palette),
            OperationalHistoryAvailability.Empty => MessageBlock(section.EmptyMessage, palette),
            _ => BuildScrollableRows(section.Rows, palette)
        };

        return expander;
    }

    private static bool ResolveExpanded(
        OperationalHistorySectionPresentation section,
        string projectId,
        IDictionary<string, bool> expandedByProject)
    {
        if (expandedByProject.TryGetValue(projectId, out var remembered))
        {
            return remembered;
        }

        return section.ExpandByDefault;
    }

    private static UIElement BuildScrollableRows(
        IReadOnlyList<OperationalHistoryRowPresentation> rows,
        ThemePalette palette)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var row in rows)
        {
            panel.Children.Add(BuildRow(row, palette));
        }

        return new ScrollViewer
        {
            Content = panel,
            MaxHeight = MaxExpandedHistoryHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Padding = new Thickness(0),
            Focusable = false
        };
    }

    private static UIElement BuildRow(OperationalHistoryRowPresentation row, ThemePalette palette)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 1, 0, 1),
            ToolTip = row.ToolTip,
            ClipToBounds = true
        };
        // Fixed columns keep scan alignment; summary star column ellipsizes long text.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddCell(grid, 0, row.TimeLabel, palette, opacity: 0.7, mono: true);
        AddCell(grid, 1, $"{row.SourceGlyph} {row.SourceLabel}", palette, opacity: 0.85, bold: true);

        var summary = new TextBlock
        {
            FontSize = 10,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Top
        };
        // History Failed is row emphasis only — overall footer still uses current MonitorHealth.
        summary.Inlines.Add(new Run(row.PrimaryText)
        {
            Foreground = StatusPanelVisuals.EmphasisBrushForHistory(row.Emphasis, palette),
            FontWeight = row.Emphasis == StatusPanelRowEmphasis.Error
                ? FontWeights.SemiBold
                : FontWeights.Normal
        });
        if (!string.IsNullOrWhiteSpace(row.SecondaryText))
        {
            summary.Inlines.Add(new Run($" · {row.SecondaryText}")
            {
                Foreground = new SolidColorBrush(palette.Foreground) { Opacity = 0.65 }
            });
        }

        Grid.SetColumn(summary, 2);
        grid.Children.Add(summary);
        return grid;
    }

    private static void AddCell(
        Grid grid,
        int column,
        string text,
        ThemePalette palette,
        double opacity,
        bool bold = false,
        bool mono = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontFamily = mono ? new MediaFontFamily("Consolas") : System.Windows.SystemFonts.MessageFontFamily,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = opacity,
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static UIElement MessageBlock(string text, ThemePalette palette) =>
        new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = 0.65,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
}
