using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace BuildMonitor.TrayApp.Services;

internal static class StatusPanelVisuals
{
    internal static IBuildSourceLinkOpener? LinkOpener { get; set; }

    internal static Action<StatusPanelProjectLogRequest>? OpenProjectLog { get; set; }

    internal static Action<string, string, TextBlock>? RegisterBuildSourceAgeCell { get; set; }

    internal static Action<string, string, BuildSourceStatusCellHandle>? RegisterBuildSourceStatusCell { get; set; }

    internal sealed class BuildSourceStatusCellHandle(TextBlock block, string projectId)
    {
        internal void Apply(BuildSourcePresentationRow row, ThemePalette palette)
        {
            var statusText = $"{row.StatusGlyph} {row.StatusText}";
            block.FontWeight = FontWeights.SemiBold;
            block.Foreground = EmphasisBrush(row.Emphasis, palette);
            block.Inlines.Clear();
            block.Text = null;

            var localLogAction = StatusPanelLocalStatusActionRules.Resolve(row);
            if (localLogAction != LocalBuildStatusLogAction.None)
            {
                var link = new Hyperlink(new Run(statusText))
                {
                    ToolTip = localLogAction == LocalBuildStatusLogAction.OpenLogWithErrorsFilter
                        ? "View build log (errors)"
                        : "View build log (warnings)",
                    TextDecorations = null,
                    Foreground = LinkBrush(palette),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                link.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    OpenProjectLog?.Invoke(new StatusPanelProjectLogRequest(
                        projectId,
                        SelectErrors: localLogAction == LocalBuildStatusLogAction.OpenLogWithErrorsFilter,
                        SelectWarnings: localLogAction == LocalBuildStatusLogAction.OpenLogWithWarningsFilter));
                };
                block.Inlines.Add(link);
                return;
            }

            block.Text = statusText;
        }
    }

    private sealed record BuildSourceLinkClickTag(
        string ProjectId,
        AzureBuildLinkTarget Target,
        AzureBuildFailureNavigationRequest? FailureRequest,
        AzureBuildBranchNavigationRequest? BranchRequest);

    public static UIElement BuildStepProgressChart(IReadOnlyList<BuildProgressStep> steps, ThemePalette palette)
    {
        var container = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };

        container.Children.Add(new TextBlock
        {
            Text = "Build pipeline",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Opacity = 0.9,
            Margin = new Thickness(0, 0, 0, 3)
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

        var legend = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
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

    /// <summary>Dense LOCAL grid: label/value pairs use horizontal space (Mode/Build | Errors/Warnings).</summary>
    public static UIElement BuildLocalDenseGrid(
        IReadOnlyList<StatusPanelStatusRow> rows,
        ThemePalette palette) =>
        BuildDetailRows(rows, palette);

    public static UIElement BuildDetailRows(
        IReadOnlyList<StatusPanelStatusRow> rows,
        ThemePalette palette)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[i];
            AddDenseLabel(grid, i, 0, ToTitleCaseLabel(row.Label), palette);
            AddDenseValue(grid, i, 1, row.Primary, EmphasisBrush(row.Emphasis, palette), row.ToolTip);
            if (!string.IsNullOrWhiteSpace(row.Secondary))
            {
                AddDenseValue(
                    grid,
                    i,
                    2,
                    row.Secondary!,
                    new SolidColorBrush(palette.Foreground) { Opacity = 0.75 },
                    null);
            }
        }

        return grid;
    }

    private static string ToTitleCaseLabel(string label) =>
        label.ToUpperInvariant() switch
        {
            "MODE" => "Mode",
            "AGENT" => "Agent",
            "CHANGES" => "Changes",
            "BUILD" => "Build",
            "LAST BUILD" => "Last build",
            _ => label
        };

    public static UIElement BuildBuildsTable(
        IReadOnlyList<BuildSourcePresentationRow> rows,
        string projectId,
        ThemePalette palette)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 44 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star), MinWidth = 72 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 48, MaxWidth = 110 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 36 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 84 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 28 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 52 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 44 });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddAzureHeaderCell(grid, 0, 0, "Source", palette);
        AddAzureHeaderCell(grid, 0, 1, "Status", palette);
        AddAzureHeaderCell(grid, 0, 2, "Branch", palette);
        AddAzureHeaderCell(grid, 0, 3, "Run", palette);
        AddAzureHeaderCell(grid, 0, 4, "Build No.", palette);
        AddAzureHeaderCell(grid, 0, 5, "PR", palette);
        AddAzureHeaderCell(grid, 0, 6, "Age", palette);
        AddAzureHeaderCell(grid, 0, 7, "Issues", palette);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowIndex = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddAzureCell(grid, rowIndex, 0, row.Source, palette.Foreground, bold: true, runUrl: null, projectId, allowEllipsis: false, palette: palette);
            AddBuildStatusCell(grid, rowIndex, 1, row, projectId, palette);
            AddBuildLinkCell(grid, rowIndex, 2, row.BranchDisplay, row.AzureNavigation?.Branch, row.AzureNavigation?.FailureRequest, row.AzureNavigation?.BranchRequest, projectId, palette, allowEllipsis: true);
            AddBuildLinkCell(grid, rowIndex, 3, row.RunDisplay, row.AzureNavigation?.Run, row.AzureNavigation?.FailureRequest, null, projectId, palette, allowEllipsis: false);
            AddBuildLinkCell(grid, rowIndex, 4, row.BuildNumberDisplay, row.AzureNavigation?.BuildNumber, row.AzureNavigation?.FailureRequest, null, projectId, palette, allowEllipsis: false);
            AddBuildLinkCell(grid, rowIndex, 5, row.PullRequestDisplay, row.AzureNavigation?.PullRequest, row.AzureNavigation?.FailureRequest, null, projectId, palette, allowEllipsis: false);
            var ageCell = AddAzureCell(grid, rowIndex, 6, row.AgeDisplay, palette.Foreground, bold: false, null, projectId, allowEllipsis: true, palette: palette);
            RegisterBuildSourceAgeCell?.Invoke(projectId, row.Source, ageCell);
            AddAzureCell(grid, rowIndex, 7, row.IssuesDisplay, palette.Foreground, bold: false, null, projectId, allowEllipsis: false, palette: palette);

            if (!string.IsNullOrWhiteSpace(row.AttentionNote))
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var noteRow = grid.RowDefinitions.Count - 1;
                var note = new TextBlock
                {
                    Text = row.AttentionNote,
                    FontSize = 9,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(200, 80, 60)),
                    Margin = new Thickness(0, 0, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetRow(note, noteRow);
                Grid.SetColumn(note, 1);
                Grid.SetColumnSpan(note, 7);
                grid.Children.Add(note);
            }
        }

        return grid;
    }

    private static void AddBuildStatusCell(
        Grid grid,
        int row,
        int column,
        BuildSourcePresentationRow data,
        string projectId,
        ThemePalette palette)
    {
        var statusText = $"{data.StatusGlyph} {data.StatusText}";
        var localLogAction = StatusPanelLocalStatusActionRules.Resolve(data);
        UIElement content;
        if (localLogAction != LocalBuildStatusLogAction.None)
        {
            content = CreateLocalStatusLogContent(
                statusText,
                projectId,
                localLogAction,
                EmphasisBrush(data.Emphasis, palette),
                palette);
        }
        else
        {
            content = CreateBuildLinkContent(
                statusText,
                data.AzureNavigation?.Status,
                data.AzureNavigation?.FailureRequest,
                null,
                projectId,
                EmphasisBrush(data.Emphasis, palette),
                fontWeight: FontWeights.SemiBold,
                margin: new Thickness(0, 0, 6, 1),
                allowEllipsis: true,
                toolTip: data.AzureNavigation?.Status.Kind == AzureBuildLinkKind.FailureDetails
                    ? "Open failure details in Azure DevOps"
                    : "Open in Azure DevOps");
        }

        PlaceBuildCell(grid, row, column, content);

        if (string.Equals(data.Source, "Local", StringComparison.OrdinalIgnoreCase)
            && content is Border { Child: TextBlock statusBlock })
        {
            RegisterBuildSourceStatusCell?.Invoke(
                projectId,
                data.Source,
                new BuildSourceStatusCellHandle(statusBlock, projectId));
        }
    }

    private static UIElement CreateLocalStatusLogContent(
        string statusText,
        string projectId,
        LocalBuildStatusLogAction action,
        SolidColorBrush foreground,
        ThemePalette palette)
    {
        var block = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground,
            Margin = new Thickness(0, 0, 6, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };

        var link = new Hyperlink(new Run(statusText))
        {
            ToolTip = action == LocalBuildStatusLogAction.OpenLogWithErrorsFilter
                ? "View build log (errors)"
                : "View build log (warnings)",
            TextDecorations = null,
            Foreground = LinkBrush(palette),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        link.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            OpenProjectLog?.Invoke(new StatusPanelProjectLogRequest(
                projectId,
                SelectErrors: action == LocalBuildStatusLogAction.OpenLogWithErrorsFilter,
                SelectWarnings: action == LocalBuildStatusLogAction.OpenLogWithWarningsFilter));
        };
        block.Inlines.Add(link);

        return new Border
        {
            Child = block,
            ClipToBounds = true,
            Background = System.Windows.Media.Brushes.Transparent
        };
    }

    private static void AddBuildLinkCell(
        Grid grid,
        int row,
        int column,
        string text,
        AzureBuildLinkTarget? target,
        AzureBuildFailureNavigationRequest? failureRequest,
        AzureBuildBranchNavigationRequest? branchRequest,
        string projectId,
        ThemePalette palette,
        bool allowEllipsis)
    {
        var isClickable = target is not null && IsClickableBuildLink(target, failureRequest, branchRequest);
        var content = CreateBuildLinkContent(
            text,
            target,
            failureRequest,
            branchRequest,
            projectId,
            isClickable ? LinkBrush(palette) : new SolidColorBrush(palette.Foreground),
            fontWeight: FontWeights.Normal,
            margin: new Thickness(0, 0, 8, 1),
            allowEllipsis: allowEllipsis,
            toolTip: "Open in Azure DevOps");

        PlaceBuildCell(grid, row, column, content);
    }

    private static UIElement CreateBuildLinkContent(
        string text,
        AzureBuildLinkTarget? target,
        AzureBuildFailureNavigationRequest? failureRequest,
        AzureBuildBranchNavigationRequest? branchRequest,
        string projectId,
        SolidColorBrush foreground,
        FontWeight fontWeight,
        Thickness margin,
        bool allowEllipsis,
        string? toolTip)
    {
        var block = new TextBlock
        {
            FontSize = 10,
            FontWeight = fontWeight,
            Foreground = foreground,
            Margin = margin,
            TextTrimming = allowEllipsis ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap
        };

        if (target is not null && IsClickableBuildLink(target, failureRequest, branchRequest))
        {
            block.Text = null;
            block.Inlines.Add(CreateBuildHyperlink(text, foreground, projectId, target, failureRequest, branchRequest, toolTip));
        }
        else
        {
            block.Text = text;
        }

        return new Border
        {
            Child = block,
            ClipToBounds = true,
            Background = System.Windows.Media.Brushes.Transparent
        };
    }

    private static Hyperlink CreateBuildHyperlink(
        string text,
        SolidColorBrush foreground,
        string projectId,
        AzureBuildLinkTarget target,
        AzureBuildFailureNavigationRequest? failureRequest,
        AzureBuildBranchNavigationRequest? branchRequest,
        string? toolTip)
    {
        var link = new Hyperlink(new Run(text))
        {
            ToolTip = toolTip,
            TextDecorations = null,
            Foreground = foreground,
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = new BuildSourceLinkClickTag(projectId, target, failureRequest, branchRequest)
        };
        link.PreviewMouseLeftButtonDown += OnBuildHyperlinkClick;
        return link;
    }

    private static void PlaceBuildCell(Grid grid, int row, int column, UIElement content)
    {
        Grid.SetRow(content, row);
        Grid.SetColumn(content, column);
        grid.Children.Add(content);
    }

    private static void OnBuildHyperlinkClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Hyperlink { Tag: BuildSourceLinkClickTag tag })
        {
            return;
        }

        e.Handled = true;
        NavigateBuildLink(tag.ProjectId, tag.Target, tag.FailureRequest, tag.BranchRequest);
    }

    private static bool IsClickableBuildLink(
        AzureBuildLinkTarget target,
        AzureBuildFailureNavigationRequest? failureRequest,
        AzureBuildBranchNavigationRequest? branchRequest)
    {
        if (target.Kind == AzureBuildLinkKind.None)
        {
            return false;
        }

        if (target.Kind == AzureBuildLinkKind.FailureDetails)
        {
            return failureRequest is not null;
        }

        if (target.Kind == AzureBuildLinkKind.Branch)
        {
            return branchRequest is not null;
        }

        return !string.IsNullOrWhiteSpace(target.Uri)
            && Uri.TryCreate(target.Uri, UriKind.Absolute, out _);
    }

    private static void NavigateBuildLink(
        string projectId,
        AzureBuildLinkTarget target,
        AzureBuildFailureNavigationRequest? failureRequest,
        AzureBuildBranchNavigationRequest? branchRequest)
    {
        if (target.Kind == AzureBuildLinkKind.FailureDetails && failureRequest is not null)
        {
            LinkOpener?.OpenFailureDetailsAsync(failureRequest);
            return;
        }

        if (target.Kind == AzureBuildLinkKind.Branch && branchRequest is not null)
        {
            LinkOpener?.OpenBranchAsync(branchRequest);
            return;
        }

        if (string.IsNullOrWhiteSpace(target.Uri)
            || !HttpUriNavigationValidator.TryParseAllowed(target.Uri, out var uri)
            || uri is null)
        {
            return;
        }

        OpenProjectUri(projectId, uri);
    }

    private static void OpenProjectUri(string projectId, Uri uri)
    {
        if (!HttpUriNavigationValidator.IsAllowedNavigationUri(uri))
        {
            return;
        }

        LinkOpener?.OpenUri(projectId, uri);
    }

    private static void AddDenseLabel(Grid grid, int row, int column, string text, ThemePalette palette)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.55,
            Foreground = new SolidColorBrush(palette.Foreground),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 6, 1)
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private static void AddDenseValue(
        Grid grid,
        int row,
        int column,
        string text,
        SolidColorBrush brush,
        string? toolTip)
    {
        var value = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 8, 1)
        };
        if (!string.IsNullOrWhiteSpace(toolTip))
        {
            value.ToolTip = toolTip;
        }

        Grid.SetRow(value, row);
        Grid.SetColumn(value, column);
        grid.Children.Add(value);
    }

    public static UIElement BuildOverallFooterSummary(
        MonitorHealth overallHealth,
        string overallLabel,
        ThemePalette palette)
    {
        var health = overallHealth;
        var label = overallLabel;
        var glyph = health switch
        {
            MonitorHealth.Red => "●",
            MonitorHealth.Amber => "●",
            MonitorHealth.Green => "●",
            _ => "○"
        };
        var color = health switch
        {
            MonitorHealth.Red => WpfColor.FromRgb(220, 53, 69),
            MonitorHealth.Amber => WpfColor.FromRgb(255, 193, 7),
            MonitorHealth.Green => WpfColor.FromRgb(40, 167, 69),
            _ => WpfColor.FromRgb(160, 168, 180)
        };

        var panel = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Overall",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.65,
            Foreground = new SolidColorBrush(palette.Foreground),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return panel;
    }

    public static UIElement BuildStatusRows(
        IReadOnlyList<StatusPanelStatusRow> rows,
        ThemePalette palette) =>
        BuildDetailRows(rows, palette);

    private static SolidColorBrush EmphasisBrush(StatusPanelRowEmphasis emphasis, ThemePalette palette) =>
        emphasis switch
        {
            StatusPanelRowEmphasis.Error => new SolidColorBrush(WpfColor.FromRgb(240, 96, 96)),
            StatusPanelRowEmphasis.Warning => new SolidColorBrush(WpfColor.FromRgb(255, 193, 7)),
            // Activity (Building / Queued) — amber, same for Local and Azure (not source-blue).
            StatusPanelRowEmphasis.Active => new SolidColorBrush(WpfColor.FromRgb(255, 193, 7)),
            StatusPanelRowEmphasis.Busy => new SolidColorBrush(WpfColor.FromRgb(255, 193, 7)),
            StatusPanelRowEmphasis.Success => new SolidColorBrush(WpfColor.FromRgb(72, 199, 116)),
            // Neutral / cancelled / unknown — muted, high-contrast on dark panels.
            _ => new SolidColorBrush(palette.Foreground) { Opacity = 0.88 }
        };

    internal static SolidColorBrush EmphasisBrushForHistory(StatusPanelRowEmphasis emphasis, ThemePalette palette) =>
        EmphasisBrush(emphasis, palette);

    /// <summary>High-contrast link colour for identifiers (Run / Build No. / PR / Branch), not status semantics.</summary>
    private static SolidColorBrush LinkBrush(ThemePalette palette) =>
        new(WpfColor.FromRgb(
            (byte)Math.Clamp(palette.Accent.R + 40, 0, 255),
            (byte)Math.Clamp(palette.Accent.G + 30, 0, 255),
            (byte)Math.Clamp(palette.Accent.B + 20, 0, 255)));

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

    public static UIElement BuildSiteReadyBlock(string listenUrl, string projectId, ThemePalette palette)
    {
        var canonicalUrl = listenUrl;
        var readyGreen = WpfColor.FromRgb(40, 167, 69);
        var row = new DockPanel { LastChildFill = true };
        row.Children.Add(new TextBlock
        {
            Text = "Site ready",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(readyGreen),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        DockPanel.SetDock(row.Children[0], Dock.Left);

        var linkBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        if (HttpUriNavigationValidator.TryParseAllowed(canonicalUrl, out var uri) && uri is not null)
        {
            var link = new Hyperlink
            {
                Foreground = new SolidColorBrush(palette.Accent),
                FontWeight = FontWeights.SemiBold,
                TextDecorations = TextDecorations.Underline,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            link.Inlines.Add($"Open {canonicalUrl}");
            link.Foreground = LinkBrush(palette);
            link.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                OpenProjectUri(projectId, uri);
            };
            linkBlock.Inlines.Add(link);
        }
        else
        {
            linkBlock.Inlines.Add(new Run(canonicalUrl)
            {
                Foreground = new SolidColorBrush(palette.Foreground),
                FontWeight = FontWeights.SemiBold
            });
        }

        row.Children.Add(linkBlock);

        return new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(readyGreen),
            Background = new SolidColorBrush(Blend(readyGreen, palette.CardBackground, 0.88f)),
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Child = row
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
            Margin = new Thickness(0, 4, 0, 1)
        };

    public static UIElement BuildAzureSection(AzureStatusPresentation azure, string projectId, ThemePalette palette)
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
            panel.Children.Add(BuildAzureTable(azure.Rows, projectId, palette));
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

    private static UIElement BuildAzureTable(IReadOnlyList<AzureStatusTableRow> rows, string projectId, ThemePalette palette)
    {
        var grid = new Grid();
        // Pipeline/Branch flexible; Status/Run/Build No./PR hug content so the row stays dense.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star), MinWidth = 88 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 72 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 56, MaxWidth = 140 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 40 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 88 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 32 });

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

            AddAzureCell(grid, rowIndex, 0, row.Pipeline, palette.Foreground, bold: true, row.RunUrl, projectId, allowEllipsis: true, asIdentifierLink: true, palette: palette);
            AddAzureStatusCell(grid, rowIndex, 1, row, projectId, palette);
            AddAzureCell(grid, rowIndex, 2, row.Branch, palette.Foreground, bold: false, row.RunUrl, projectId, allowEllipsis: true, asIdentifierLink: true, palette: palette);
            AddAzureCell(grid, rowIndex, 3, row.RunDisplay, palette.Foreground, bold: false, row.RunUrl, projectId, allowEllipsis: false, asIdentifierLink: true, palette: palette);
            AddAzureCell(grid, rowIndex, 4, row.BuildNumberDisplay, palette.Foreground, bold: false, row.RunUrl, projectId, allowEllipsis: false, asIdentifierLink: true, palette: palette);
            AddAzureCell(grid, rowIndex, 5, row.PullRequestDisplay, palette.Foreground, bold: false, row.RunUrl, projectId, allowEllipsis: false, asIdentifierLink: true, palette: palette);
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

    private static TextBlock AddAzureCell(
        Grid grid,
        int row,
        int column,
        string text,
        WpfColor color,
        bool bold,
        string? runUrl,
        string projectId,
        bool allowEllipsis = true,
        bool asIdentifierLink = false,
        ThemePalette? palette = null)
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

        if (HttpUriNavigationValidator.TryParseAllowed(runUrl, out var uri) && uri is not null)
        {
            var linkBrush = asIdentifierLink && palette is not null
                ? LinkBrush(palette)
                : new SolidColorBrush(color);
            var link = new Hyperlink(new Run(text))
            {
                ToolTip = "Open in Azure DevOps",
                TextDecorations = null,
                Foreground = linkBrush,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            link.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                OpenProjectUri(projectId, uri);
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
        return block;
    }

    private static void AddAzureStatusCell(Grid grid, int row, int column, AzureStatusTableRow data, string projectId, ThemePalette palette)
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
        if (HttpUriNavigationValidator.TryParseAllowed(data.RunUrl, out var uri) && uri is not null)
        {
            var statusBrush = EmphasisBrush(data.Emphasis, palette);
            var link = new Hyperlink(new Run(statusText))
            {
                ToolTip = "Open in Azure DevOps",
                TextDecorations = null,
                Foreground = statusBrush,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            link.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                OpenProjectUri(projectId, uri);
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
