using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.LocalBuild;
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

    private static WpfColor Blend(WpfColor from, WpfColor to, float amount)
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
        var openUrl = LocalPortProbe.NormalizeBrowserUrl(listenUrl);
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
                        Text = openUrl,
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
        var openUrl = LocalPortProbe.NormalizeBrowserUrl(listenUrl);
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
        if (Uri.TryCreate(openUrl, UriKind.Absolute, out var uri))
        {
            var link = new Hyperlink
            {
                NavigateUri = uri,
                Foreground = new SolidColorBrush(palette.Accent),
                FontWeight = FontWeights.SemiBold,
                TextDecorations = TextDecorations.Underline,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            link.Inlines.Add($"Open {openUrl}");
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
            linkRow.Inlines.Add(new Run(openUrl)
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
}
