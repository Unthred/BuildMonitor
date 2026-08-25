using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildMonitor.Core.Models;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace BuildMonitor.TrayApp.Services;

internal static class StatusPanelVisuals
{
    public static UIElement BuildStepProgressChart(IReadOnlyList<BuildProgressStep> steps, ThemePalette palette)
    {
        var container = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        container.Children.Add(new TextBlock
        {
            Text = "Build pipeline",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = 0.9,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var bar = new Grid { Height = 8 };
        foreach (var _ in steps)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var segment = new Border
            {
                Background = StepBrush(steps[i].Status, palette),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
                ToolTip = steps[i].Label
            };
            Grid.SetColumn(segment, i);
            bar.Children.Add(segment);
        }

        container.Children.Add(bar);

        var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var step in steps)
        {
            legend.Children.Add(new TextBlock
            {
                Text = FormatLegendStep(step),
                FontSize = 10,
                Foreground = StepBrush(step.Status, palette),
                Margin = new Thickness(0, 0, 10, 2)
            });
        }

        container.Children.Add(legend);
        return container;
    }

    public static UIElement BuildStatusRows(
        IReadOnlyList<StatusPanelStatusRow> rows,
        ThemePalette palette)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[i];
            var primaryBrush = EmphasisBrush(row.Emphasis, palette);

            var label = new TextBlock
            {
                Text = row.Label,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.55,
                Foreground = new SolidColorBrush(palette.Foreground),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 6, 1)
            };
            Grid.SetRow(label, i);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var primary = new TextBlock
            {
                Text = row.Primary,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = primaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 8, 1)
            };
            if (!string.IsNullOrWhiteSpace(row.ToolTip))
            {
                primary.ToolTip = row.ToolTip;
            }

            Grid.SetRow(primary, i);
            Grid.SetColumn(primary, 1);
            grid.Children.Add(primary);

            if (!string.IsNullOrWhiteSpace(row.Secondary))
            {
                var secondary = new TextBlock
                {
                    Text = row.Secondary,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(palette.Foreground) { Opacity = 0.75 },
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = WpfHorizontalAlignment.Right,
                    Margin = new Thickness(0, 1, 0, 1)
                };
                if (!string.IsNullOrWhiteSpace(row.ToolTip))
                {
                    secondary.ToolTip = row.ToolTip;
                }

                Grid.SetRow(secondary, i);
                Grid.SetColumn(secondary, 2);
                grid.Children.Add(secondary);
            }
        }

        return grid;
    }

    private static SolidColorBrush EmphasisBrush(StatusPanelRowEmphasis emphasis, ThemePalette palette) =>
        emphasis switch
        {
            StatusPanelRowEmphasis.Error => new SolidColorBrush(WpfColor.FromRgb(220, 53, 69)),
            StatusPanelRowEmphasis.Warning => new SolidColorBrush(WpfColor.FromRgb(180, 120, 0)),
            StatusPanelRowEmphasis.Active => new SolidColorBrush(WpfColor.FromRgb(0, 123, 255)),
            StatusPanelRowEmphasis.Busy => new SolidColorBrush(WpfColor.FromRgb(120, 90, 200)),
            _ => new SolidColorBrush(palette.Foreground)
        };

    public static UIElement BuildIssueSummary(int errors, int warnings, ThemePalette palette)
    {
        var container = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        container.Children.Add(IssueChip(
            errors,
            errors == 1 ? "error" : "errors",
            WpfColor.FromRgb(220, 53, 69),
            palette,
            errors > 0));
        container.Children.Add(IssueChip(
            warnings,
            warnings == 1 ? "warning" : "warnings",
            WpfColor.FromRgb(255, 193, 7),
            palette,
            warnings > 0));
        return container;
    }

    public static UIElement BuildActivityIndicator(ProjectLifecycleState state, ThemePalette palette)
    {
        var label = state switch
        {
            ProjectLifecycleState.Building => "Build in progress",
            ProjectLifecycleState.Testing => "Tests in progress",
            ProjectLifecycleState.Watching => "Watch rebuild active",
            _ => "Activity"
        };

        var accent = state switch
        {
            ProjectLifecycleState.Testing => WpfColor.FromRgb(0, 123, 255),
            _ => WpfColor.FromRgb(255, 193, 7)
        };

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = 0.9,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var track = new Grid
        {
            Height = 6,
            Background = new SolidColorBrush(palette.Border) { Opacity = 0.35 }
        };
        track.Children.Add(new WpfRectangle
        {
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            Width = 72,
            Fill = new SolidColorBrush(accent),
            RadiusX = 2,
            RadiusY = 2
        });
        panel.Children.Add(track);
        return panel;
    }

    private static UIElement IssueChip(
        int count,
        string label,
        WpfColor accent,
        ThemePalette palette,
        bool emphasized)
    {
        var foreground = emphasized
            ? accent
            : palette.Foreground;
        var background = emphasized
            ? Blend(accent, palette.CardBackground, 0.82f)
            : Blend(palette.Border, palette.CardBackground, 0.55f);

        return new Border
        {
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(emphasized ? accent : palette.Border) { Opacity = emphasized ? 0.85 : 0.5 },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock
            {
                Text = $"{count:N0} {label}",
                FontSize = 11,
                FontWeight = emphasized ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(foreground) { Opacity = emphasized ? 1 : 0.65 }
            }
        };
    }

    internal static WpfColor Blend(WpfColor from, WpfColor to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        var r = (byte)(from.R + (to.R - from.R) * amount);
        var g = (byte)(from.G + (to.G - from.G) * amount);
        var b = (byte)(from.B + (to.B - from.B) * amount);
        return WpfColor.FromRgb(r, g, b);
    }

    private static string FormatLegendStep(BuildProgressStep step) =>
        step.Status switch
        {
            BuildStepStatus.Complete => $"✓ {step.Label}",
            BuildStepStatus.Active => $"● {step.Label}",
            BuildStepStatus.Failed => $"✗ {step.Label}",
            _ => $"○ {step.Label}"
        };

    private static SolidColorBrush StepBrush(BuildStepStatus status, ThemePalette palette) =>
        status switch
        {
            BuildStepStatus.Complete => new SolidColorBrush(WpfColor.FromRgb(40, 167, 69)),
            BuildStepStatus.Active => new SolidColorBrush(WpfColor.FromRgb(255, 193, 7)),
            BuildStepStatus.Failed => new SolidColorBrush(WpfColor.FromRgb(220, 53, 69)),
            _ => new SolidColorBrush(palette.Foreground) { Opacity = 0.35 }
        };

    public static UIElement BuildSiteAwaitingBlock(string listenUrl, ThemePalette palette)
    {
        var canonicalUrl = listenUrl;
        var accent = WpfColor.FromRgb(255, 193, 7);
        return new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(accent) { Opacity = 0.75 },
            Background = new SolidColorBrush(Blend(accent, palette.CardBackground, 0.88f)),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Site starting…",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(accent)
                    },
                    new TextBlock
                    {
                        Text = canonicalUrl,
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(palette.Foreground) { Opacity = 0.75 },
                        Margin = new Thickness(0, 2, 0, 0)
                    }
                }
            }
        };
    }

    public static UIElement BuildSiteReadyBlock(string listenUrl, ThemePalette palette)
    {
        var canonicalUrl = listenUrl;
        var readyGreen = WpfColor.FromRgb(40, 167, 69);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Site ready",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(readyGreen)
        });

        var linkRow = new TextBlock { Margin = new Thickness(0, 3, 0, 0) };
        if (Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var uri))
        {
            var link = new Hyperlink
            {
                NavigateUri = uri,
                Foreground = new SolidColorBrush(palette.Accent),
                FontWeight = FontWeights.SemiBold,
                TextDecorations = TextDecorations.Underline,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            link.Inlines.Add($"Open {canonicalUrl}");
            link.RequestNavigate += (_, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch
                {
                    // Best effort only.
                }

                e.Handled = true;
            };
            linkRow.Inlines.Add(link);
        }
        else
        {
            linkRow.Inlines.Add(new Run(canonicalUrl)
            {
                Foreground = new SolidColorBrush(palette.Foreground),
                FontWeight = FontWeights.SemiBold
            });
        }

        panel.Children.Add(linkRow);

        return new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(readyGreen),
            Background = new SolidColorBrush(Blend(readyGreen, palette.CardBackground, 0.86f)),
            Child = panel
        };
    }

    public static UIElement BuildSectionHeader(string text, ThemePalette palette) =>
        new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = 0.75,
            Margin = new Thickness(0, 6, 0, 2)
        };

    public static UIElement BuildAzureSection(AzureStatusPresentation azure, ThemePalette palette)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(BuildSectionHeader(azure.HeaderLabel, palette));

        if (!azure.ShowTable)
        {
            var message = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = EmphasisBrush(azure.Emphasis, palette),
                TextWrapping = TextWrapping.Wrap
            };
            if (!string.IsNullOrWhiteSpace(azure.MessageGlyph))
            {
                message.Inlines.Add(new Run(azure.MessageGlyph + " ") { FontWeight = FontWeights.Bold });
            }

            if (!string.IsNullOrWhiteSpace(azure.MessagePrimary))
            {
                message.Inlines.Add(new Run(azure.MessagePrimary));
            }

            panel.Children.Add(message);

            if (!string.IsNullOrWhiteSpace(azure.MessageSecondary))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = azure.MessageSecondary,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(palette.Foreground),
                    Opacity = 0.85,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(14, 1, 0, 0)
                });
            }
        }
        else
        {
            panel.Children.Add(BuildAzureTable(azure.Rows, palette));
        }

        if (!string.IsNullOrWhiteSpace(azure.AttentionLine))
        {
            panel.Children.Add(new TextBlock
            {
                Text = azure.AttentionLine,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(200, 80, 60)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        return panel;
    }

    private static UIElement BuildAzureTable(IReadOnlyList<AzureStatusTableRow> rows, ThemePalette palette)
    {
        var grid = new Grid();
        // Star for pipeline/branch (ellipsis OK); Auto for Run / Build No. / PR so normal ids stay fully visible.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star), MinWidth = 110 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 96 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 90 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 44 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 96 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 36 });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddAzureHeaderCell(grid, 0, 0, "Pipeline", palette);
        AddAzureHeaderCell(grid, 0, 1, "Status", palette);
        AddAzureHeaderCell(grid, 0, 2, "Branch", palette);
        AddAzureHeaderCell(grid, 0, 3, "Run", palette);
        AddAzureHeaderCell(grid, 0, 4, "Build No.", palette);
        AddAzureHeaderCell(grid, 0, 5, "PR", palette);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowIndex = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddAzureCell(grid, rowIndex, 0, row.Pipeline, palette.Foreground, bold: true, row.RunUrl, allowEllipsis: true);
            AddAzureStatusCell(grid, rowIndex, 1, row, palette);
            AddAzureCell(grid, rowIndex, 2, row.Branch, palette.Foreground, bold: false, row.RunUrl, allowEllipsis: true);
            AddAzureCell(grid, rowIndex, 3, row.RunDisplay, palette.Foreground, bold: false, row.RunUrl, allowEllipsis: false);
            AddAzureCell(grid, rowIndex, 4, row.BuildNumberDisplay, palette.Foreground, bold: false, row.RunUrl, allowEllipsis: false);
            AddAzureCell(grid, rowIndex, 5, row.PullRequestDisplay, palette.Foreground, bold: false, row.RunUrl, allowEllipsis: false);
        }

        return grid;
    }

    private static void AddAzureHeaderCell(Grid grid, int row, int column, string text, ThemePalette palette)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 6, 1),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static void AddAzureCell(
        Grid grid,
        int row,
        int column,
        string text,
        WpfColor color,
        bool bold,
        string? runUrl,
        bool allowEllipsis = true)
    {
        var block = new TextBlock
        {
            FontSize = 10,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 8, 1),
            TextTrimming = allowEllipsis ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap
        };

        if (!string.IsNullOrWhiteSpace(runUrl) && Uri.TryCreate(runUrl, UriKind.Absolute, out var uri))
        {
            var link = new Hyperlink(new Run(text))
            {
                NavigateUri = uri,
                ToolTip = "Open in Azure DevOps",
                TextDecorations = null
            };
            link.RequestNavigate += (_, e) =>
            {
                e.Handled = true;
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch
                {
                    // ignore launch failures
                }
            };
            block.Inlines.Add(link);
        }
        else
        {
            block.Text = text;
        }

        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static void AddAzureStatusCell(Grid grid, int row, int column, AzureStatusTableRow data, ThemePalette palette)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 6, 1) };
        var status = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = EmphasisBrush(data.Emphasis, palette),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        var statusText = $"{data.StatusGlyph} {data.StatusText}";
        if (!string.IsNullOrWhiteSpace(data.RunUrl) && Uri.TryCreate(data.RunUrl, UriKind.Absolute, out var uri))
        {
            var link = new Hyperlink(new Run(statusText))
            {
                NavigateUri = uri,
                ToolTip = "Open in Azure DevOps",
                TextDecorations = null
            };
            link.RequestNavigate += (_, e) =>
            {
                e.Handled = true;
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch
                {
                    // ignore
                }
            };
            status.Inlines.Add(link);
        }
        else
        {
            status.Text = statusText;
        }

        stack.Children.Add(status);
        if (!string.IsNullOrWhiteSpace(data.TimingText))
        {
            stack.Children.Add(new TextBlock
            {
                Text = data.TimingText,
                FontSize = 9,
                Foreground = new SolidColorBrush(palette.Foreground),
                Opacity = 0.7,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Grid.SetRow(stack, row);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }
}
