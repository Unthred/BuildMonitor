using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.TrayApp.Services;
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
    private readonly DispatcherTimer accentAnimTimer;
    private double accentAnimPhase;
    private IReadOnlyList<ProjectHealthSnapshot> lastSnapshots = [];
    private StatusPanelPresentation? lastRenderedPresentation;
    private bool deferCardRebuildUntilMouseLeave;
    private DateTimeOffset? panelDismissAtUtc;
    private Rectangle? lastTrayIconBounds;
    private IntPtr lastTrayIconWindowHandle;
    private Rectangle? lastPlacementBounds;
    private double lastFittedBodyHeight;
    private double lastPlacedLeft = double.NaN;
    private double lastPlacedTop = double.NaN;
    private bool fitLayoutPending;
    private bool repositionAfterFit;

    public event Action<string>? ViewLogRequested;
    public event Action<string>? CopyErrorsRequested;
    public event Action<string>? RestartAppRequested;
    public event Action<string>? RebuildAndRestartRequested;
    public event Action<string>? RunTestsRequested;
    public event Action<string>? MarkStillEditingRequested;
    public event Action? CloseRequested;

    private bool followVirtualDesktop = true;

    public bool FollowVirtualDesktop
    {
        get => followVirtualDesktop;
        set => followVirtualDesktop = value;
    }

    public HoverStatusPanel()
    {
        InitializeComponent();
        ScheduleFitPanelToContent(repositionAfter: true);
        countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdownTimer.Tick += (_, _) => OnCountdownTick();
        accentAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        accentAnimTimer.Tick += (_, _) => OnAccentAnimTick();
        HeaderStillEditingButton.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (HeaderStillEditingButton.Tag is string projectId)
            {
                MarkStillEditingRequested?.Invoke(projectId);
            }

            e.Handled = true;
        };
        MouseLeave += (_, _) =>
        {
            if (!deferCardRebuildUntilMouseLeave)
            {
                return;
            }

            deferCardRebuildUntilMouseLeave = false;
            Update(lastSnapshots, panelDismissAtUtc);
        };
        Closed += (_, _) =>
        {
            countdownTimer.Stop();
            accentAnimTimer.Stop();
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke();

    public void ApplyTheme(ResolvedTheme theme)
    {
        currentTheme = theme;
        var palette = ThemeService.GetPalette(theme);
        PanelBorder.Background = BrushFromResource("ThemeBackgroundBrush", palette.Background);
        PanelBorder.BorderBrush = BrushFromResource("ThemeBorderBrush", palette.Border);
        Foreground = BrushFromResource("ThemeForegroundBrush", palette.Foreground);
        CloseButton.Foreground = BrushFromResource("ThemeForegroundBrush", palette.Foreground);
        ThemeService.ApplyChrome(this, theme == ResolvedTheme.Dark);
        AppIconService.ApplyToWindow(this);
        var presentation = StatusPanelPresentationBuilder.Build(
            lastSnapshots,
            panelDismissAtUtc,
            DateTimeOffset.UtcNow);
        ApplySideRail(presentation.SideRail, palette);
    }

    private static SolidColorBrush BrushFromResource(string key, WpfColor fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as SolidColorBrush
        ?? new SolidColorBrush(fallback);

    public void PrepareForPendingRebuild()
    {
        deferCardRebuildUntilMouseLeave = false;
        lastRenderedPresentation = null;
        lastFittedBodyHeight = 0;
        panelDismissAtUtc = null;
        ApplyHeaderCountdownText(string.Empty);
        ApplyHeaderStillEditing(null, null);
    }

    public void Update(IReadOnlyList<ProjectHealthSnapshot> snapshots, DateTimeOffset? dismissAtUtc = null)
    {
        lastSnapshots = snapshots;
        panelDismissAtUtc = dismissAtUtc;
        var palette = ThemeService.GetPalette(currentTheme);
        var presentation = StatusPanelPresentationBuilder.Build(snapshots, dismissAtUtc, DateTimeOffset.UtcNow);

        var rebuildCards = StatusPanelPresentationChangeDetector.RequiresCardRebuild(
            lastRenderedPresentation,
            presentation);
        var urgentCardRebuild = StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(
            lastRenderedPresentation,
            presentation);
        if (IsMouseOver && rebuildCards && !urgentCardRebuild)
        {
            deferCardRebuildUntilMouseLeave = true;
            rebuildCards = false;
        }
        else if (rebuildCards)
        {
            deferCardRebuildUntilMouseLeave = false;
        }

        if (rebuildCards)
        {
            RebuildProjectCards(presentation.Cards, presentation.SideRail, palette);
            lastRenderedPresentation = presentation;
            ScheduleFitPanelToContent(repositionAfter: true);
        }

        HeaderText.Text = presentation.ActiveProjectCount switch
        {
            0 => "Build status",
            1 => "Build status",
            _ => $"Build status ({presentation.ActiveProjectCount})"
        };

        ApplySideRail(presentation.SideRail, palette);
        ApplyHeaderCountdownText(presentation.HeaderCountdownText);
        ApplyHeaderStillEditing(presentation.HeaderStillEditingProjectId, presentation.HeaderStillEditingToolTip);
        SyncCountdownTimer(snapshots);
    }

    private void RebuildProjectCards(
        IReadOnlyList<StatusPanelCardPresentation> cards,
        StatusPanelSideRailPresentation sideRail,
        ThemePalette palette)
    {
        ProjectCards.Items.Clear();

        foreach (var cardModel in cards)
        {
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(palette.Border),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(palette.CardBackground)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = cardModel.DisplayName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(palette.Foreground),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (cardModel.BuildSourceRows is { Count: > 0 })
            {
                panel.Children.Add(StatusPanelVisuals.BuildSectionHeader("BUILDS", palette));
                panel.Children.Add(StatusPanelVisuals.BuildBuildsTable(cardModel.BuildSourceRows, cardModel.ProjectId, palette));
            }

            if (cardModel.StatusRows.Count > 0)
            {
                panel.Children.Add(StatusPanelVisuals.BuildSectionHeader("DETAIL", palette));
                panel.Children.Add(StatusPanelVisuals.BuildDetailRows(cardModel.StatusRows, palette));
            }

            if (cardModel.BuildSourceRows is not { Count: > 0 }
                && cardModel.Azure is { ShowSection: true })
            {
                panel.Children.Add(StatusPanelVisuals.BuildAzureSection(cardModel.Azure, cardModel.ProjectId, palette));
            }

            if (!string.IsNullOrWhiteSpace(cardModel.CurrentActionText))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = cardModel.CurrentActionText,
                    Foreground = new SolidColorBrush(palette.Foreground),
                    Opacity = 0.8,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            if (cardModel.ShowSiteReady && cardModel.ListenUrl is not null)
            {
                panel.Children.Add(StatusPanelVisuals.BuildSiteReadyBlock(cardModel.ListenUrl, cardModel.ProjectId, palette));
            }
            else if (cardModel.ShowSiteAwaiting && cardModel.ListenUrl is not null)
            {
                panel.Children.Add(StatusPanelVisuals.BuildSiteAwaitingBlock(cardModel.ListenUrl, palette));
            }

            if (cardModel.ShowProgressChart)
            {
                panel.Children.Add(StatusPanelVisuals.BuildStepProgressChart(cardModel.ProgressSteps, palette));
            }
            else if (cardModel.ShowErrorPreview && cardModel.ErrorPreview is not null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = cardModel.ErrorPreview,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(220, 53, 69)),
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }
            else if (cardModel.ShowActivityIndicator)
            {
                panel.Children.Add(StatusPanelVisuals.BuildActivityIndicator(cardModel.ActivityState, palette));
            }

            var actionRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var actions = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            var viewLog = new WpfButton
            {
                Content = "Log",
                ToolTip = "View log",
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 10,
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 4, 0),
                Tag = cardModel.ProjectId
            };
            WireActionButton(viewLog, () => ViewLogRequested?.Invoke(cardModel.ProjectId));
            actions.Children.Add(viewLog);

            if (cardModel.ShowCopyErrorsButton)
            {
                var copyErrors = new WpfButton
                {
                    Content = "Errors",
                    ToolTip = "Copy error lines from the latest build, run, or test log",
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 4, 0),
                    Tag = cardModel.ProjectId
                };
                WireActionButton(copyErrors, () => CopyErrorsRequested?.Invoke(cardModel.ProjectId));
                actions.Children.Add(copyErrors);
            }

            if (cardModel.ShowRestartButtons)
            {
                var restart = new WpfButton
                {
                    Content = "Restart",
                    ToolTip = "Stop and start run/watch without rebuilding",
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 4, 0),
                    HorizontalAlignment = WpfHorizontalAlignment.Left,
                    Tag = cardModel.ProjectId
                };
                WireActionButton(restart, () => RestartAppRequested?.Invoke(cardModel.ProjectId));
                actions.Children.Add(restart);

                var rebuildRestart = new WpfButton
                {
                    Content = "Rebuild",
                    ToolTip = "Full build, then start run/watch",
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 4, 0),
                    HorizontalAlignment = WpfHorizontalAlignment.Left,
                    Tag = cardModel.ProjectId
                };
                WireActionButton(rebuildRestart, () => RebuildAndRestartRequested?.Invoke(cardModel.ProjectId));
                actions.Children.Add(rebuildRestart);
            }

            if (cardModel.ShowRunTestsButton)
            {
                var runTests = new WpfButton
                {
                    Content = "Tests",
                    ToolTip = "Run tests",
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 4, 0),
                    HorizontalAlignment = WpfHorizontalAlignment.Left,
                    Tag = cardModel.ProjectId
                };
                WireActionButton(runTests, () => RunTestsRequested?.Invoke(cardModel.ProjectId));
                actions.Children.Add(runTests);
            }

            Grid.SetColumn(actions, 0);
            actionRow.Children.Add(actions);

            var overall = StatusPanelVisuals.BuildOverallFooterSummary(sideRail, palette);
            Grid.SetColumn(overall, 1);
            actionRow.Children.Add(overall);

            panel.Children.Add(actionRow);
            System.Windows.Controls.Panel.SetZIndex(actionRow, 10);

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

    private static void WireActionButton(WpfButton button, Action invoke)
    {
        // Use Click (not PreviewMouseLeftButtonDown) so Button chrome receives the
        // routed event reliably; hyperlinks keep PreviewMouse for #97 stability.
        button.Click += (_, _) => invoke();
    }

    private void ApplyHeaderCountdownText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            PanelHeaderCountdown.Visibility = Visibility.Collapsed;
            PanelHeaderCountdown.Text = string.Empty;
            return;
        }

        PanelHeaderCountdown.Text = text;
        PanelHeaderCountdown.Visibility = Visibility.Visible;
    }

    private void ApplyHeaderStillEditing(string? projectId, string? toolTip)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            HeaderStillEditingButton.Visibility = Visibility.Collapsed;
            HeaderStillEditingButton.Tag = null;
            HeaderStillEditingButton.ToolTip = "AI agent still working — extend the rebuild wait";
            return;
        }

        HeaderStillEditingButton.Tag = projectId;
        HeaderStillEditingButton.ToolTip = string.IsNullOrWhiteSpace(toolTip)
            ? "AI agent still working — extend the rebuild wait"
            : toolTip;
        HeaderStillEditingButton.Visibility = Visibility.Visible;
    }

    private void OnCountdownTick()
    {
        if (!IsVisible)
        {
            countdownTimer.Stop();
            return;
        }

        var presentation = StatusPanelPresentationBuilder.Build(
            lastSnapshots,
            panelDismissAtUtc,
            DateTimeOffset.UtcNow);
        ApplyHeaderCountdownText(presentation.HeaderCountdownText);
        ApplyHeaderStillEditing(presentation.HeaderStillEditingProjectId, presentation.HeaderStillEditingToolTip);

        var needsCountdown = HasActiveRebuildCountdown(lastSnapshots) || panelDismissAtUtc is not null;
        if (!needsCountdown)
        {
            countdownTimer.Stop();
        }
    }

    private void SyncCountdownTimer(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        if (IsVisible && (HasActiveRebuildCountdown(snapshots) || panelDismissAtUtc is not null))
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

    private static bool HasActiveRebuildCountdown(IReadOnlyList<ProjectHealthSnapshot> snapshots) =>
        snapshots.Any(s =>
            StatusPanelBuildVisibilityEvaluator.HasActiveRebuildCountdown(s, DateTimeOffset.UtcNow));

    private void ApplySideRail(StatusPanelSideRailPresentation sideRail, ThemePalette palette)
    {
        // Overall health is shown in the action footer; keep the legacy rail collapsed.
        _ = palette;
        _ = sideRail;
        AccentColumn.Width = new GridLength(0);
        SideRail.Width = 0;
        SideRail.Visibility = Visibility.Collapsed;
        ActiveAccentContent.Visibility = Visibility.Collapsed;
        IdleStatusContent.Visibility = Visibility.Collapsed;
        accentAnimTimer.Stop();
    }

    private static WpfColor AccentColorForHealth(MonitorHealth health) =>
        health switch
        {
            MonitorHealth.Red => WpfColor.FromRgb(220, 53, 69),
            MonitorHealth.Amber => WpfColor.FromRgb(255, 193, 7),
            MonitorHealth.Green => WpfColor.FromRgb(40, 167, 69),
            _ => WpfColor.FromRgb(108, 117, 125)
        };

    private void ApplyAccentRailTheme(WpfColor accent, ThemePalette palette)
    {
        var railBackground = StatusPanelVisuals.Blend(accent, palette.CardBackground, 0.9f);
        var glow = StatusPanelVisuals.Blend(accent, palette.Background, 0.55f);
        SideRail.Background = new SolidColorBrush(railBackground);
        SideRail.BorderBrush = new SolidColorBrush(accent) { Opacity = 0.55 };
        AccentGlow.Fill = new SolidColorBrush(glow);
        AccentRingOuter.Stroke = new SolidColorBrush(accent);
        AccentRingInner.Stroke = new SolidColorBrush(accent) { Opacity = 0.75 };
        AccentCore.Fill = new SolidColorBrush(accent);
        AccentLabel.Foreground = new SolidColorBrush(accent);
        var spark = new SolidColorBrush(accent);
        AccentSparkA.Fill = spark;
        AccentSparkB.Fill = new SolidColorBrush(accent) { Opacity = 0.8 };
        AccentSparkC.Fill = new SolidColorBrush(accent) { Opacity = 0.65 };
    }

    private void OnAccentAnimTick()
    {
        if (ActiveAccentContent.Visibility != Visibility.Visible)
        {
            accentAnimTimer.Stop();
            return;
        }

        accentAnimPhase += 1;
        OuterRingRotate.Angle = accentAnimPhase * 2.8;
        InnerRingRotate.Angle = -accentAnimPhase * 4.2;
        var pulse = 0.55 + 0.45 * Math.Sin(accentAnimPhase * 0.14);
        AccentCore.Opacity = pulse;
        AccentGlow.Opacity = 0.22 + 0.18 * Math.Sin(accentAnimPhase * 0.1);
        AccentSparkA.Opacity = 0.45 + 0.55 * Math.Sin(accentAnimPhase * 0.2);
        AccentSparkB.Opacity = 0.35 + 0.5 * Math.Sin(accentAnimPhase * 0.17 + 1.2);
        AccentSparkC.Opacity = 0.3 + 0.45 * Math.Sin(accentAnimPhase * 0.23 + 2.1);
    }

    private void ApplyIdleRailTheme(StatusPanelSideRailPresentation sideRail, ThemePalette palette)
    {
        var health = sideRail.IdleHealth;
        var webReady = sideRail.ShowWebReadyBadge;
        var accent = AccentColorForHealth(health);
        var railBackground = StatusPanelVisuals.Blend(accent, palette.CardBackground, 0.92f);
        var glow = StatusPanelVisuals.Blend(accent, palette.Background, 0.6f);

        SideRail.Background = new SolidColorBrush(railBackground);
        SideRail.BorderBrush = new SolidColorBrush(accent) { Opacity = 0.45 };
        IdleRailGlow.Fill = new SolidColorBrush(glow);
        IdleHousing.Fill = new SolidColorBrush(StatusPanelVisuals.Blend(palette.CardBackground, palette.Border, 0.15f));
        IdleHousing.Stroke = new SolidColorBrush(palette.Border);

        ApplyIdleLamp(IdleLampRed, WpfColor.FromRgb(235, 65, 80), WpfColor.FromRgb(120, 55, 62), health == MonitorHealth.Red);
        ApplyIdleLamp(IdleLampAmber, WpfColor.FromRgb(255, 205, 0), WpfColor.FromRgb(140, 115, 50), health == MonitorHealth.Amber);
        ApplyIdleLamp(
            IdleLampGreen,
            WpfColor.FromRgb(50, 205, 90),
            WpfColor.FromRgb(55, 110, 75),
            health is MonitorHealth.Green or MonitorHealth.Unknown);

        IdleOverallCaption.Foreground = new SolidColorBrush(health == MonitorHealth.Unknown ? palette.Foreground : accent);
        IdleStatusLabel.Text = sideRail.IdleLabel;
        IdleStatusLabel.Foreground = new SolidColorBrush(health == MonitorHealth.Unknown ? palette.Foreground : accent);

        if (webReady && health == MonitorHealth.Green)
        {
            IdleWebReadyBadge.Visibility = Visibility.Visible;
            IdleWebReadyBadge.Fill = new SolidColorBrush(WpfColor.FromRgb(77, 163, 255));
            IdleWebReadyBadge.Stroke = new SolidColorBrush(WpfColor.FromRgb(220, 235, 255));
            IdleWebReadyBadge.StrokeThickness = 1;
        }
        else
        {
            IdleWebReadyBadge.Visibility = Visibility.Collapsed;
        }
    }

    private static void ApplyIdleLamp(System.Windows.Shapes.Ellipse lamp, WpfColor active, WpfColor inactive, bool isActive)
    {
        lamp.Fill = new SolidColorBrush(isActive ? active : inactive);
        lamp.Opacity = isActive ? 1.0 : 0.42;
    }

    private void ApplyFixedPanelSize() => ScheduleFitPanelToContent(repositionAfter: true);

    private void ScheduleFitPanelToContent(bool repositionAfter = false)
    {
        if (repositionAfter)
        {
            repositionAfterFit = true;
        }

        if (fitLayoutPending)
        {
            return;
        }

        fitLayoutPending = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                fitLayoutPending = false;
                var sizeChanged = FitPanelToContent();
                var shouldReposition = sizeChanged || repositionAfterFit;
                repositionAfterFit = false;
                if (shouldReposition)
                {
                    ApplyTrayPlacementIfNeeded(force: true);
                }
            });
    }

    private bool FitPanelToContent()
    {
        Width = StatusPanelLayout.WindowWidth;
        MinWidth = StatusPanelLayout.WindowMinWidth;
        MaxWidth = StatusPanelLayout.WindowMaxWidth;
        HeaderRow.Height = new GridLength(StatusPanelLayout.HeaderRowHeight);
        AccentColumn.Width = new GridLength(0);
        SideRail.Width = 0;
        SideRail.Visibility = Visibility.Collapsed;

        ProjectCards.UpdateLayout();
        ProjectCards.Measure(new WpfSize(StatusPanelLayout.ContentMeasureWidth, double.PositiveInfinity));
        var bodyHeight = Math.Ceiling(Math.Clamp(
            Math.Max(StatusPanelLayout.MinBodyHeight, ProjectCards.DesiredSize.Height),
            StatusPanelLayout.MinBodyHeight,
            StatusPanelLayout.MaxBodyHeight()));

        if (lastFittedBodyHeight > 0 && Math.Abs(bodyHeight - lastFittedBodyHeight) < 1)
        {
            return false;
        }

        lastFittedBodyHeight = bodyHeight;
        BodyRow.Height = new GridLength(bodyHeight);
        var windowHeight = StatusPanelLayout.VerticalChrome + bodyHeight;
        Height = windowHeight;
        MinHeight = windowHeight;
        MaxHeight = windowHeight;

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BuildMonitor",
                "hover-panel-size.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                $"Width={Width};ActualWidth={ActualWidth};MinWidth={MinWidth};MaxWidth={MaxWidth};Utc={DateTimeOffset.UtcNow:O}");
        }
        catch
        {
            // diagnostic only
        }

        return true;
    }

    public void ApplyLayout(WindowLayoutState layout)
    {
        // Panel height is content-driven; persisted width is ignored.
        _ = layout;
    }

    public void CaptureLayout(WindowLayoutState layout) =>
        WindowLayoutService.Capture(this, layout, sizeOnly: true);

    /// <summary>
    /// Clears cached tray placement so the next show/follow recomputes against the current display layout.
    /// </summary>
    public void InvalidateTrayPlacementCache()
    {
        lastTrayIconBounds = null;
        lastPlacementBounds = null;
        lastTrayIconWindowHandle = IntPtr.Zero;
        lastPlacedLeft = double.NaN;
        lastPlacedTop = double.NaN;
    }

    public void ShowNearTray(Rectangle? trayIconBounds = null, IntPtr trayIconWindowHandle = default)
    {
        if (trayIconBounds is { Width: > 0, Height: > 0 } bounds)
        {
            lastTrayIconBounds = bounds;
        }

        if (trayIconWindowHandle != IntPtr.Zero)
        {
            lastTrayIconWindowHandle = trayIconWindowHandle;
        }

        if (!IsVisible)
        {
            Show();
        }

        EnsureOnTrayDesktop();
        var presentation = StatusPanelPresentationBuilder.Build(
            lastSnapshots,
            panelDismissAtUtc,
            DateTimeOffset.UtcNow);
        ApplySideRail(presentation.SideRail, ThemeService.GetPalette(currentTheme));
        ApplyHeaderCountdownText(presentation.HeaderCountdownText);
        ApplyHeaderStillEditing(presentation.HeaderStillEditingProjectId, presentation.HeaderStillEditingToolTip);
        ScheduleFitPanelToContent(repositionAfter: true);
        SyncCountdownTimer(lastSnapshots);
    }

    public void FollowTray(Rectangle? trayIconBounds, IntPtr trayIconWindowHandle = default)
    {
        var boundsChanged = false;
        if (trayIconBounds is { Width: > 0, Height: > 0 } bounds)
        {
            boundsChanged = lastTrayIconBounds != bounds;
            lastTrayIconBounds = bounds;
        }

        if (trayIconWindowHandle != IntPtr.Zero)
        {
            lastTrayIconWindowHandle = trayIconWindowHandle;
        }

        if (!IsVisible)
        {
            return;
        }

        EnsureOnTrayDesktop();

        if (boundsChanged)
        {
            ApplyTrayPlacementIfNeeded(force: true);
        }
    }

    private void EnsureOnTrayDesktop()
    {
        if (!followVirtualDesktop)
        {
            return;
        }

        var panelHandle = new WindowInteropHelper(this).Handle;
        if (panelHandle == IntPtr.Zero)
        {
            return;
        }

        VirtualDesktopInterop.TryFollowCurrentVirtualDesktop(panelHandle);
    }

    private void ApplyTrayPlacementIfNeeded(bool force = false)
    {
        if (lastTrayIconBounds is { Width: > 0, Height: > 0 } bounds)
        {
            const double margin = 12;
            var area = System.Windows.Forms.Screen.FromRectangle(bounds).WorkingArea;
            var width = Width;
            var height = Height;
            var maxBottom = bounds.Top - margin;
            var top = maxBottom - height;
            var left = bounds.Left + ((bounds.Width - width) / 2.0);
            left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width));
            top = Math.Clamp(top, area.Top, Math.Max(area.Top, maxBottom - height));

            if (!force
                && lastPlacementBounds == bounds
                && !double.IsNaN(lastPlacedLeft)
                && Math.Abs(lastPlacedLeft - left) < 1
                && Math.Abs(lastPlacedTop - top) < 1)
            {
                return;
            }

            Left = lastPlacedLeft = left;
            Top = lastPlacedTop = top;
            lastPlacementBounds = bounds;
            return;
        }

        if (force || double.IsNaN(lastPlacedLeft))
        {
            TrayScreenPlacement.PlaceNearTrayBottomRight(this);
            lastPlacedLeft = Left;
            lastPlacedTop = Top;
            lastPlacementBounds = null;
        }
    }
}
