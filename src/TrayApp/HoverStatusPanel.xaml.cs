using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.TrayApp.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace BuildMonitor.TrayApp;

public partial class HoverStatusPanel : Window
{
    private ResolvedTheme currentTheme = ResolvedTheme.Light;

    public event Action<string>? ViewLogRequested;
    public event Action<string>? CopyErrorsRequested;
    public event Action<string>? RestartAppRequested;
    public event Action<string>? RebuildAndRestartRequested;
    public event Action<string>? RunTestsRequested;

    public HoverStatusPanel()
    {
        InitializeComponent();
    }

    public void ApplyTheme(ResolvedTheme theme)
    {
        currentTheme = theme;
        var palette = ThemeService.GetPalette(theme);
        PanelBorder.Background = BrushFromResource("ThemeBackgroundBrush", palette.Background);
        PanelBorder.BorderBrush = BrushFromResource("ThemeBorderBrush", palette.Border);
        Foreground = BrushFromResource("ThemeForegroundBrush", palette.Foreground);
        ThemeService.ApplyChrome(this, theme == ResolvedTheme.Dark);
        AppIconService.ApplyToWindow(this);
    }

    private static SolidColorBrush BrushFromResource(string key, WpfColor fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as SolidColorBrush
        ?? new SolidColorBrush(fallback);

    public void Update(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        var palette = ThemeService.GetPalette(currentTheme);
        ProjectCards.Items.Clear();

        foreach (var snapshot in snapshots.Where(s => s.IsActive))
        {
            var healthBrush = HealthBrush(snapshot.Health, palette);

            var card = new Border
            {
                BorderBrush = new SolidColorBrush(palette.Border),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8),
                Background = new SolidColorBrush(palette.CardBackground)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = snapshot.DisplayName,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(palette.Foreground)
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"{snapshot.HealthLabel} — {snapshot.State}",
                Foreground = healthBrush,
                Margin = new Thickness(0, 4, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = snapshot.IsRestarting
                    ? "Restarting app…"
                    : snapshot.IssueCountsText
                        ?? $"Errors: {snapshot.ErrorCount} | Warnings: {snapshot.WarningCount}",
                Foreground = new SolidColorBrush(palette.Foreground),
                Opacity = 0.85,
                Margin = new Thickness(0, 2, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = FormatLastBuildLine(snapshot),
                Foreground = new SolidColorBrush(palette.Foreground),
                Opacity = 0.85,
                Margin = new Thickness(0, 2, 0, 0)
            });

            if (snapshot.SupportsAppRestart
                && !string.IsNullOrWhiteSpace(snapshot.ListenUrl)
                && ShouldShowListenUrl(snapshot))
            {
                var showLink = snapshot.ListenUrlReady
                    && snapshot.State is ProjectLifecycleState.Running or ProjectLifecycleState.Watching;
                panel.Children.Add(showLink
                    ? BuildListenUrlBlock(snapshot.ListenUrl, palette)
                    : BuildListenUrlPendingBlock(snapshot.ListenUrl, palette));
            }

            if (snapshot.ProgressSteps.Count > 0)
            {
                panel.Children.Add(BuildProgressPanel(snapshot.ProgressSteps, palette));
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.LastErrorPreview))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = snapshot.LastErrorPreview,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(220, 53, 69)),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            var actions = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0)
            };

            var viewLog = new WpfButton
            {
                Content = "View log",
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                Tag = snapshot.ProjectId
            };
            viewLog.Click += (_, _) => ViewLogRequested?.Invoke(snapshot.ProjectId);
            actions.Children.Add(viewLog);

            if (snapshot.ErrorCount > 0)
            {
                var copyErrors = new WpfButton
                {
                    Content = "Copy errors",
                    Margin = new Thickness(8, 0, 0, 0),
                    HorizontalAlignment = WpfHorizontalAlignment.Left,
                    Tag = snapshot.ProjectId,
                    ToolTip = "Copy error lines from the latest build, run, or test log"
                };
                copyErrors.Click += (_, _) => CopyErrorsRequested?.Invoke(snapshot.ProjectId);
                actions.Children.Add(copyErrors);
            }

            if (snapshot.SupportsAppRestart)
            {
                var restart = new WpfButton
                {
                    Content = "Restart app",
                    Margin = new Thickness(8, 0, 0, 0),
                    HorizontalAlignment = WpfHorizontalAlignment.Left,
                    Tag = snapshot.ProjectId,
                    ToolTip = "Stop and start run/watch without rebuilding"
                };
                restart.Click += (_, _) => RestartAppRequested?.Invoke(snapshot.ProjectId);
                actions.Children.Add(restart);

                var rebuildRestart = new WpfButton
                {
                    Content = "Rebuild & restart",
                    Margin = new Thickness(8, 0, 0, 0),
                    HorizontalAlignment = WpfHorizontalAlignment.Left,
                    Tag = snapshot.ProjectId,
                    ToolTip = "Full build, then start run/watch"
                };
                rebuildRestart.Click += (_, _) => RebuildAndRestartRequested?.Invoke(snapshot.ProjectId);
                actions.Children.Add(rebuildRestart);
            }

            var runTests = new WpfButton
            {
                Content = "Run tests",
                Margin = new Thickness(8, 0, 0, 0),
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                Tag = snapshot.ProjectId
            };
            runTests.Click += (_, _) => RunTestsRequested?.Invoke(snapshot.ProjectId);
            actions.Children.Add(runTests);

            panel.Children.Add(actions);

            card.Child = panel;
            ProjectCards.Items.Add(card);
        }

        if (ProjectCards.Items.Count == 0)
        {
            ProjectCards.Items.Add(new TextBlock
            {
                Text = "No active projects. Enable projects in Settings → Projects.",
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private static bool ShouldShowListenUrl(ProjectHealthSnapshot snapshot) =>
        snapshot.IsRestarting
        || snapshot.State is ProjectLifecycleState.Running
            or ProjectLifecycleState.Watching
            or ProjectLifecycleState.Building;

    private static UIElement BuildListenUrlPendingBlock(string listenUrl, ThemePalette palette) =>
        new TextBlock
        {
            Text = $"Starting {listenUrl}…",
            Foreground = new SolidColorBrush(palette.Foreground) { Opacity = 0.7 },
            Margin = new Thickness(0, 2, 0, 0)
        };

    private static UIElement BuildListenUrlBlock(string listenUrl, ThemePalette palette)
    {
        var openUrl = LocalPortProbe.NormalizeBrowserUrl(listenUrl);
        var labelBrush = new SolidColorBrush(palette.Foreground) { Opacity = 0.9 };
        var block = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
        block.Inlines.Add(new Run("URL: ") { Foreground = labelBrush });

        if (!Uri.TryCreate(openUrl, UriKind.Absolute, out var uri))
        {
            block.Inlines.Add(new Run(openUrl) { Foreground = labelBrush });
            return block;
        }

        var link = new Hyperlink
        {
            NavigateUri = uri,
            Foreground = new SolidColorBrush(palette.Accent),
            TextDecorations = TextDecorations.Underline,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        link.Inlines.Add(openUrl);
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
        block.Inlines.Add(link);
        return block;
    }

    private static string FormatLastBuildLine(ProjectHealthSnapshot snapshot)
    {
        var isBuilding = snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing;
        if (snapshot.LastBuildFinishedAtUtc is { } finished)
        {
            var time = BuildTimestampFormatter.FormatLocalShort(finished);
            return isBuilding ? $"Last build: {time} (in progress…)" : $"Last build: {time}";
        }

        return isBuilding ? "Build in progress…" : "Last build: —";
    }

    private static UIElement BuildProgressPanel(IReadOnlyList<BuildProgressStep> steps, ThemePalette palette)
    {
        var container = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        container.Children.Add(new TextBlock
        {
            Text = "Progress",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var step in steps)
        {
            container.Children.Add(new TextBlock
            {
                Text = FormatProgressStep(step),
                Foreground = StepBrush(step.Status, palette),
                FontFamily = new WpfFontFamily("Segoe UI"),
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 0)
            });
        }

        return container;
    }

    private static string FormatProgressStep(BuildProgressStep step) =>
        step.Status switch
        {
            BuildStepStatus.Active => $"● {step.Label}",
            BuildStepStatus.Complete => $"✓ {step.Label}",
            BuildStepStatus.Failed => $"✗ {step.Label}",
            _ => $"○ {step.Label}"
        };

    private static SolidColorBrush StepBrush(BuildStepStatus status, ThemePalette palette) =>
        status switch
        {
            BuildStepStatus.Complete => new SolidColorBrush(WpfColor.FromRgb(40, 167, 69)),
            BuildStepStatus.Active => new SolidColorBrush(WpfColor.FromRgb(255, 193, 7)),
            BuildStepStatus.Failed => new SolidColorBrush(WpfColor.FromRgb(220, 53, 69)),
            _ => new SolidColorBrush(palette.Foreground) { Opacity = 0.55 }
        };

    private static WpfBrush HealthBrush(MonitorHealth health, ThemePalette palette) =>
        health switch
        {
            MonitorHealth.Green => new SolidColorBrush(WpfColor.FromRgb(40, 167, 69)),
            MonitorHealth.Amber => new SolidColorBrush(WpfColor.FromRgb(255, 193, 7)),
            MonitorHealth.Red => new SolidColorBrush(WpfColor.FromRgb(220, 53, 69)),
            _ => new SolidColorBrush(palette.Foreground)
        };

    public void ApplyLayout(WindowLayoutState layout)
    {
        if (layout.Width >= MinWidth && !double.IsNaN(layout.Width))
        {
            Width = layout.Width;
        }

        if (layout.Height >= MinHeight && !double.IsNaN(layout.Height))
        {
            Height = layout.Height;
        }
    }

    public void CaptureLayout(WindowLayoutState layout) =>
        WindowLayoutService.Capture(this, layout, sizeOnly: true);

    public void ShowNearTray()
    {
        TrayScreenPlacement.PlaceNearTrayBottomRight(this);
        if (!IsVisible)
        {
            Show();
        }
    }
}
