using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.TrayApp.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSize = System.Windows.Size;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace BuildMonitor.TrayApp;

public partial class HoverStatusPanel : Window
{
    private ResolvedTheme currentTheme = ResolvedTheme.Light;
    private readonly DispatcherTimer countdownTimer;
    private IReadOnlyList<ProjectHealthSnapshot> lastSnapshots = [];
    private Rectangle? lastTrayIconBounds;

    public event Action<string>? ViewLogRequested;
    public event Action<string>? CopyErrorsRequested;
    public event Action<string>? RestartAppRequested;
    public event Action<string>? RebuildAndRestartRequested;
    public event Action<string>? RunTestsRequested;

    public HoverStatusPanel()
    {
        InitializeComponent();
        countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdownTimer.Tick += (_, _) => OnCountdownTick();
        Closed += (_, _) => countdownTimer.Stop();
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
        lastSnapshots = snapshots;
        var palette = ThemeService.GetPalette(currentTheme);
        ProjectCards.Items.Clear();

        foreach (var snapshot in snapshots.Where(s => s.IsActive))
        {
            var healthBrush = HealthBrush(snapshot.Health, palette);

            var card = new Border
            {
                BorderBrush = new SolidColorBrush(palette.Border),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 5),
                Padding = new Thickness(6, 5, 6, 5),
                Background = new SolidColorBrush(palette.CardBackground)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = snapshot.DisplayName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(palette.Foreground)
            });

            var statusLine = snapshot.IsRestarting
                ? "Restarting app…"
                : snapshot.State == ProjectLifecycleState.WaitingForEdits
                    ? $"Waiting — {snapshot.HealthLabel}"
                    : $"{snapshot.HealthLabel} — {snapshot.State}";
            var issueSuffix = snapshot.IssueCountsText
                ?? (snapshot.ErrorCount > 0 || snapshot.WarningCount > 0
                    ? $" · {snapshot.ErrorCount}e / {snapshot.WarningCount}w"
                    : string.Empty);
            panel.Children.Add(new TextBlock
            {
                Text = statusLine + issueSuffix,
                Foreground = healthBrush,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = FormatLastBuildLine(snapshot),
                Foreground = new SolidColorBrush(palette.Foreground),
                Opacity = 0.8,
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 0)
            });

            if (!string.IsNullOrWhiteSpace(snapshot.EditGatingDetailText)
                || snapshot.RebuildQuietUntilUtc is not null)
            {
                var countdown = EditGatingDetailFormatter.FormatCountdownRemaining(
                    snapshot.RebuildQuietUntilUtc,
                    DateTimeOffset.UtcNow);
                if (!string.IsNullOrWhiteSpace(snapshot.EditGatingDetailText))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = snapshot.EditGatingDetailText,
                        Foreground = new SolidColorBrush(palette.Foreground),
                        Opacity = 0.9,
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 3, 0, 0)
                    });
                }

                if (!string.IsNullOrWhiteSpace(countdown))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = countdown,
                        Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 193, 7)),
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 11,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
            }

            if (StatusPanelBuildVisibilityEvaluator.ShouldShowSiteStatus(snapshot))
            {
                panel.Children.Add(snapshot.ListenUrlReady
                    ? StatusPanelVisuals.BuildSiteReadyBlock(snapshot.ListenUrl!, palette)
                    : StatusPanelVisuals.BuildSiteAwaitingBlock(snapshot.ListenUrl!, palette));
            }

            if (snapshot.ProgressSteps.Count > 0
                && snapshot.State is ProjectLifecycleState.Building
                    or ProjectLifecycleState.Testing
                    or ProjectLifecycleState.BuildFailed)
            {
                panel.Children.Add(StatusPanelVisuals.BuildStepProgressChart(snapshot.ProgressSteps, palette));
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.LastErrorPreview))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = snapshot.LastErrorPreview,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(220, 53, 69)),
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }
            else if (snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing)
            {
                panel.Children.Add(StatusPanelVisuals.BuildActivityIndicator(snapshot.State, palette));
            }
            else if (snapshot.ErrorCount > 0 || snapshot.WarningCount > 0)
            {
                panel.Children.Add(StatusPanelVisuals.BuildIssueSummary(snapshot.ErrorCount, snapshot.WarningCount, palette));
            }

            if (snapshot.ProgressSteps.Count > 0
                && snapshot.State is ProjectLifecycleState.Building
                    or ProjectLifecycleState.Testing
                    or ProjectLifecycleState.BuildFailed
                && (snapshot.ErrorCount > 0 || snapshot.WarningCount > 0))
            {
                panel.Children.Add(StatusPanelVisuals.BuildIssueSummary(snapshot.ErrorCount, snapshot.WarningCount, palette));
            }

            var actions = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
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

        var activeCount = snapshots.Count(s => s.IsActive);
        HeaderText.Text = activeCount switch
        {
            0 => "Build status",
            1 => "Build status",
            _ => $"Build status ({activeCount})"
        };

        FitHeightToContent();
        SyncCountdownTimer(snapshots);
        ApplyTrayPlacement();
    }

    private void OnCountdownTick()
    {
        if (!IsVisible || !HasActiveCountdown(lastSnapshots))
        {
            countdownTimer.Stop();
            return;
        }

        Update(lastSnapshots);
    }

    private void SyncCountdownTimer(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        if (IsVisible && HasActiveCountdown(snapshots))
        {
            if (!countdownTimer.IsEnabled)
            {
                countdownTimer.Start();
            }
        }
        else
        {
            countdownTimer.Stop();
        }
    }

    private static bool HasActiveCountdown(IReadOnlyList<ProjectHealthSnapshot> snapshots) =>
        snapshots.Any(s =>
            s.RebuildQuietUntilUtc is { } until
            && until > DateTimeOffset.UtcNow);

    private void FitHeightToContent()
    {
        const double chrome = 40;
        var innerWidth = Math.Max(180, Width - 18);
        ProjectCards.Measure(new WpfSize(innerWidth, double.PositiveInfinity));
        var contentHeight = ProjectCards.DesiredSize.Height;
        var maxBody = MaxHeight - chrome;

        if (contentHeight <= maxBody)
        {
            CardsScroll.MaxHeight = double.PositiveInfinity;
            CardsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Height = Math.Max(MinHeight, chrome + contentHeight);
        }
        else
        {
            CardsScroll.MaxHeight = maxBody;
            CardsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Height = MaxHeight;
        }
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
        if (double.IsFinite(layout.Width) && layout.Width >= MinWidth)
        {
            Width = layout.Width;
        }
    }

    public void CaptureLayout(WindowLayoutState layout) =>
        WindowLayoutService.Capture(this, layout, sizeOnly: true);

    public void ShowNearTray(Rectangle? trayIconBounds = null)
    {
        if (trayIconBounds is { Width: > 0, Height: > 0 } bounds)
        {
            lastTrayIconBounds = bounds;
        }

        if (!IsVisible)
        {
            Show();
        }

        FitHeightToContent();
        SyncCountdownTimer(lastSnapshots);
        ApplyTrayPlacement();
    }

    private void ApplyTrayPlacement()
    {
        if (lastTrayIconBounds is { Width: > 0, Height: > 0 } bounds)
        {
            TrayScreenPlacement.PlaceAboveTrayIcon(this, bounds);
        }
        else
        {
            TrayScreenPlacement.PlaceNearTrayBottomRight(this);
        }
    }
}
