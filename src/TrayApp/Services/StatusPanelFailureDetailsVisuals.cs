using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildMonitor.Core.Models;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace BuildMonitor.TrayApp.Services;

/// <summary>Compact Failure details block for hover status cards (#111a / #111b).</summary>
internal static class StatusPanelFailureDetailsVisuals
{
    private const double MaxSectionHeight = 140;

    public static UIElement Build(
        ProjectFailureDetails details,
        string projectId,
        ThemePalette palette,
        IDictionary<string, bool> expandedByProject,
        Action<string, FailureAction> onAction)
    {
        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        root.Children.Add(new TextBlock
        {
            Text = "Failure details",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = 0.85
        });

        var primaryPanel = new StackPanel();
        primaryPanel.Children.Add(BuildReason(details.Primary, projectId, palette, onAction));

        if (details.Reasons.Count == 1)
        {
            root.Children.Add(primaryPanel);
            return root;
        }

        // Additional concurrent reasons stay behind a compact expander so the card stays readable.
        var expander = new Expander
        {
            Header = BuildAdditionalHeader(details, palette),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Margin = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(0),
            IsExpanded = ResolveExpanded(projectId, expandedByProject),
            Tag = projectId
        };

        expander.Expanded += (_, _) => expandedByProject[projectId] = true;
        expander.Collapsed += (_, _) => expandedByProject[projectId] = false;

        var more = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        for (var i = 1; i < details.Reasons.Count; i++)
        {
            more.Children.Add(BuildReason(details.Reasons[i], projectId, palette, onAction));
        }

        expander.Content = new ScrollViewer
        {
            Content = more,
            MaxHeight = MaxSectionHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Focusable = false
        };

        root.Children.Add(primaryPanel);
        root.Children.Add(expander);
        return root;
    }

    private static UIElement BuildAdditionalHeader(ProjectFailureDetails details, ThemePalette palette)
    {
        var extra = details.Reasons.Count - 1;
        var label = extra == 1
            ? $"Also · {details.Reasons[1].Title}"
            : $"Also · {extra} more failures";
        return new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground)
        };
    }

    private static bool ResolveExpanded(string projectId, IDictionary<string, bool> expandedByProject)
    {
        if (expandedByProject.TryGetValue(projectId, out var remembered))
        {
            return remembered;
        }

        return false;
    }

    private static UIElement BuildReason(
        FailureReason reason,
        string projectId,
        ThemePalette palette,
        Action<string, FailureAction> onAction)
    {
        var block = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        var titleColor = reason.Severity == FailureSeverity.Warning
            ? System.Windows.Media.Color.FromRgb(180, 120, 20)
            : System.Windows.Media.Color.FromRgb(220, 53, 69);

        block.Children.Add(new TextBlock
        {
            Text = reason.Title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(titleColor),
            TextWrapping = TextWrapping.Wrap
        });

        block.Children.Add(new TextBlock
        {
            Text = reason.ShortReason,
            FontSize = 10,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0)
        });

        if (!string.IsNullOrWhiteSpace(reason.Detail))
        {
            block.Children.Add(new TextBlock
            {
                Text = reason.Detail,
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(palette.Foreground),
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });
        }

        if (reason.Actions.Count > 0)
        {
            var actions = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 0)
            };

            foreach (var action in reason.Actions)
            {
                var button = new WpfButton
                {
                    Content = action.Label,
                    Padding = new Thickness(6, 1, 6, 1),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 4, 0),
                    Tag = projectId
                };
                var captured = action;
                button.Click += (_, _) => onAction(projectId, captured);
                actions.Children.Add(button);
            }

            block.Children.Add(actions);
        }

        return block;
    }
}
