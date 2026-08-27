using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;
using BuildMonitor.TrayApp.Services;
using Forms = System.Windows.Forms;

namespace BuildMonitor.TrayApp;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? notifyIcon;
    private Forms.ContextMenuStrip? trayContextMenu;
    private ProjectOrchestrator? orchestrator;
    private ControlPlaneHostService? controlPlaneHost;
    private BuildDiagnosticsWindow? diagnosticsWindow;
    private BuildMonitorHealthWindow? buildMonitorHealthWindow;
    private DispatcherHealthProbe? dispatcherHealthProbe;
    private HoverStatusPanel? hoverPanel;
    private DispatcherTimer? trayHoverPollTimer;
    private DispatcherTimer? statusPanelPlacementTimer;
    private SettingsStore? settingsStore;
    private AppWindowsLayoutStore? windowsLayoutStore;
    private AppSettings currentSettings = new();
    private string appDataDirectory = string.Empty;
    private DispatcherTimer? hideStatusPanelTimer;
    private DispatcherTimer? siteReadyDismissTimer;
    private DispatcherTimer? statusPanelLayoutSaveTimer;
    private bool pointerOverStatusPanel;
    private readonly Dictionary<string, MonitorHealth> previousProjectHealth = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildLogViewerWindow> openLogViewers = new(StringComparer.OrdinalIgnoreCase);
    private readonly AutoOpenLogSession autoOpenLogSession = new();
    private readonly Dictionary<string, ProjectLifecycleState> previousProjectLifecycleState =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> fileChangeBuildStarts = new(StringComparer.OrdinalIgnoreCase);
    private bool statusPanelAutoShownForBuild;
    private bool statusPanelAutoShownForEditGating;
    private bool statusPanelPinnedAutoFlow;
    private bool statusPanelDismissScheduled;
    private DateTimeOffset? statusPanelDismissAtUtc;
    private DateTimeOffset trayHoverStatusPanelSuppressedUntil = DateTimeOffset.MinValue;
    private readonly Dictionary<string, bool> previousEditGatingActive =
        new(StringComparer.OrdinalIgnoreCase);
    private WindowDisplayChangeWatcher? displayChangeWatcher;
    private readonly BuildLifecycleToastNotifier buildLifecycleToastNotifier = new();
    private readonly TrayContextMenuBuilder trayMenuBuilder = new();
    private int settingsApplyVersion;
    private readonly SemaphoreSlim settingsApplyGate = new(1, 1);
    private DispatcherTimer? buildIconAnimationTimer;
    private int buildIconAnimationFrame;
    private MonitorHealth currentTrayHealth = MonitorHealth.Unknown;
    private bool currentTrayBuilding;
    private bool currentTrayWebReady;
    private ProjectHealthSnapshot? currentTrayHeadline;
    private int exitRequested;
    private readonly object pendingHealthUiSync = new();
    private IReadOnlyList<ProjectHealthSnapshot>? pendingHealthSnapshots;
    private IReadOnlyList<ProjectHealthSnapshot> lastHealthSnapshots = [];
    private MonitorHealth pendingHealthRollup = MonitorHealth.Unknown;
    private int healthUiUpdateScheduled;
    private int trayMenuOpen;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BuildMonitor");
        AppLaunchPolicy.MigrateLegacyAppDataIfNeeded(appDataDirectory);
        Directory.CreateDirectory(appDataDirectory);

        var settingsPath = Path.Combine(appDataDirectory, "settings.json");
        var logsPath = Path.Combine(appDataDirectory, "logs");

        windowsLayoutStore = new AppWindowsLayoutStore(appDataDirectory);
        await windowsLayoutStore.LoadAsync();

        settingsStore = new SettingsStore(settingsPath);
        currentSettings = await settingsStore.LoadOrCreateDefaultAsync();

        ThemeService.ApplyTheme(currentSettings.AppBehavior.Theme);
        ToastNotificationService.ApplySettings(currentSettings.AppBehavior);
        WindowsStartupService.Apply(currentSettings.AppBehavior.RunOnLogon);

        var validationErrors = AppSettingsValidator.Validate(currentSettings);
        if (validationErrors.Count > 0)
        {
            ToastNotificationService.ShowIfEnabled(
                "Configuration issues",
                string.Join(Environment.NewLine, validationErrors),
                ToastKind.Warning,
                UserNotificationCategory.Warning);
        }

        orchestrator = new ProjectOrchestrator(logsPath, appDataDirectory);
        orchestrator.SetSettingsPersistHandler(settings =>
        {
            if (settingsStore is null)
            {
                return;
            }

            _ = settingsStore.SaveAsync(settings);
        });
        orchestrator.HealthUpdated += OnHealthUpdated;
        orchestrator.UserNotification += OnUserNotification;
        controlPlaneHost = new ControlPlaneHostService(orchestrator, appDataDirectory, RequestExit);

        WorkerHealthRegistry.Shared.Register(
            "ui.health-callback",
            "Tray health UI callback",
            TimeSpan.FromSeconds(2),
            "UI");
        dispatcherHealthProbe = new DispatcherHealthProbe(Dispatcher);

        ThemeService.ThemeChanged += OnThemeChanged;
        EnsureHoverPanel();
        ApplyThemeToUi();
        notifyIcon = BuildNotifyIcon();
        notifyIcon.Visible = true;
        displayChangeWatcher = new WindowDisplayChangeWatcher(
            Dispatcher,
            () => Volatile.Read(ref exitRequested) != 0,
            () =>
            {
                var windows = new List<Window>(openLogViewers.Count + 2);
                windows.AddRange(openLogViewers.Values);
                if (diagnosticsWindow is not null)
                {
                    windows.Add(diagnosticsWindow);
                }

                if (buildMonitorHealthWindow is not null)
                {
                    windows.Add(buildMonitorHealthWindow);
                }

                return windows;
            },
            () => hoverPanel?.InvalidateTrayPlacementCache(),
            () =>
            {
                if (hoverPanel is not { IsVisible: true })
                {
                    return;
                }

                GetTrayPlacementContext(out var trayIconBounds, out var trayWindowHandle);
                hoverPanel.FollowTray(trayIconBounds, trayWindowHandle);
            });

        if (AppLaunchPolicy.ShouldAutoOpenBuildMonitorHealth(currentSettings))
        {
            await OpenBuildMonitorHealthWhenReadyAsync();
            await WaitForUiIdleAsync();
        }

        await Task.Run(async () => await ApplySettingsAndStartAsync(
            SettingsApplyImpactClassifier.CreatePlan(before: null, currentSettings)).ConfigureAwait(false));
    }

    private async Task OpenBuildMonitorHealthWhenReadyAsync()
    {
        await Dispatcher.InvokeAsync(ShowBuildMonitorHealth, DispatcherPriority.Loaded);
        if (buildMonitorHealthWindow is not null)
        {
            await buildMonitorHealthWindow.WaitForInitialLoadAsync();
        }
    }

    private async Task ApplySettingsAndStartAsync(SettingsApplyPlan plan)
    {
        if (Volatile.Read(ref exitRequested) != 0)
        {
            return;
        }

        await settingsApplyGate.WaitAsync();
        try
        {
            if (orchestrator is null)
            {
                return;
            }

            if (plan.ResetHealthTransitionState)
            {
                previousProjectHealth.Clear();
                buildLifecycleToastNotifier.Reset();
                autoOpenLogSession.Reset();
                previousProjectLifecycleState.Clear();
                statusPanelAutoShownForBuild = false;
                fileChangeBuildStarts.Clear();
            }

            ToastNotificationService.ApplySettings(currentSettings.AppBehavior);

            if (plan.ColdStartActiveProjectsWithBuild)
            {
                await orchestrator.StopAllAsync();
            }

            if (plan.ApplyOrchestratorSettings)
            {
                orchestrator.ApplySettings(currentSettings);
                ApplyControlPlaneHost();
            }

            if (plan.ColdStartActiveProjectsWithBuild
                && AppLaunchPolicy.ShouldAutoStartAnyProjectsOnLaunch(currentSettings))
            {
                await orchestrator.StartActiveProjectsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else if (plan.RemountAffectedLocalProjectsWithoutBuild)
            {
                await orchestrator.RemountLocalProjectsWithoutBuildAsync(
                    plan.LocalRemounts,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            settingsApplyGate.Release();
        }

        _ = Dispatcher.InvokeAsync(RebuildTrayMenu, DispatcherPriority.ApplicationIdle);
    }

    private Task WaitForUiIdleAsync() =>
        Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle).Task;

    private void OnUserNotification(
        string projectId,
        string title,
        string message,
        UserNotificationKind kind,
        UserNotificationCategory category)
    {
        if (Volatile.Read(ref exitRequested) != 0)
        {
            return;
        }

        if (category == UserNotificationCategory.FileChangeDetected
            && !string.IsNullOrWhiteSpace(projectId))
        {
            fileChangeBuildStarts.Add(projectId);
        }

        Dispatcher.BeginInvoke(() =>
        {
            var toastKind = category switch
            {
                UserNotificationCategory.BuildSuccess => ToastKind.Success,
                UserNotificationCategory.BuildFailure => ToastKind.Error,
                _ => kind switch
                {
                    UserNotificationKind.Error => ToastKind.Error,
                    UserNotificationKind.Warning => ToastKind.Warning,
                    _ => ToastKind.Info
                }
            };
            ToastNotificationService.ShowIfEnabled(title, message, toastKind, category);
        });
    }

    private void OnHealthUpdated(IReadOnlyList<ProjectHealthSnapshot> snapshots, MonitorHealth rollup)
    {
        if (Volatile.Read(ref exitRequested) != 0)
        {
            return;
        }

        lock (pendingHealthUiSync)
        {
            pendingHealthSnapshots = snapshots;
            pendingHealthRollup = rollup;
        }

        if (Interlocked.CompareExchange(ref healthUiUpdateScheduled, 1, 0) != 0)
        {
            return;
        }

        // Normal (not ApplicationIdle): keep tray/status panel in step with build toasts.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, ApplyPendingHealthUi);
    }

    private void ApplyPendingHealthUi()
    {
        Interlocked.Exchange(ref healthUiUpdateScheduled, 0);

        IReadOnlyList<ProjectHealthSnapshot> snapshots;
        MonitorHealth rollup;
        lock (pendingHealthUiSync)
        {
            snapshots = pendingHealthSnapshots ?? [];
            rollup = pendingHealthRollup;
            pendingHealthSnapshots = null;
            lastHealthSnapshots = snapshots;
        }

        lock (pendingHealthUiSync)
        {
            if (pendingHealthSnapshots is not null
                && Interlocked.CompareExchange(ref healthUiUpdateScheduled, 1, 0) == 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, ApplyPendingHealthUi);
            }
        }

        if (Volatile.Read(ref exitRequested) != 0)
        {
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        WorkerHealthRegistry.Shared.SetCurrentAction("ui.health-callback", "Updating tray UI");
        try
        {
            var activeOnly = snapshots.Where(s => s.IsActive).ToList();
            currentTrayHeadline = LocalTrayIconRollupEvaluator.ChooseHeadline(activeOnly);
            UpdateTrayIcon(
                LocalTrayIconRollupEvaluator.Rollup(activeOnly),
                LocalTrayIconRollupEvaluator.IsBuilding(activeOnly),
                LocalTrayIconRollupEvaluator.IsWebReady(currentTrayHeadline));

            if (hoverPanel is { IsVisible: true })
            {
                GetTrayPlacementContext(out var trayIconBounds, out var trayWindowHandle);
                hoverPanel.FollowTray(trayIconBounds, trayWindowHandle);
                UpdateStatusPanelIfVisible(snapshots);
            }

            UpdateStatusPanelSiteReadyPin(snapshots);

            if (Volatile.Read(ref trayMenuOpen) == 0)
            {
                AutoOpenLogsOnTransition(snapshots);
                AutoShowStatusPanelWhileBuilding(snapshots);
                AutoShowStatusPanelForEditGating(snapshots);
                buildLifecycleToastNotifier.Process(snapshots, fileChangeBuildStarts);
                PlayBuildNotificationSounds(snapshots);
            }
        }
        finally
        {
            sw.Stop();
            WorkerHealthRegistry.Shared.Heartbeat(
                "ui.health-callback",
                note: "tray refresh complete",
                managedThreadId: Environment.CurrentManagedThreadId,
                workDurationMs: sw.ElapsedMilliseconds);
            WorkerHealthRegistry.Shared.SetCurrentAction("ui.health-callback", "Idle");
        }
    }

    private void AutoOpenLogsOnTransition(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots.Where(s => s.IsActive))
        {
            var project = currentSettings.Projects.FirstOrDefault(p =>
                p.Id.Equals(snapshot.ProjectId, StringComparison.OrdinalIgnoreCase));
            var mode = project?.Local?.RunOptions.AutoOpenLog ?? AutoOpenLogMode.Never;
            if (mode == AutoOpenLogMode.Never)
            {
                continue;
            }

            previousProjectLifecycleState.TryGetValue(snapshot.ProjectId, out var previousState);

            if (autoOpenLogSession.ShouldOpenViewer(mode, snapshot))
            {
                var logKind = LogKindForAutoOpen(snapshot.State, previousState);
                var (selectErrorsFilter, selectWarningsFilter) =
                    AutoOpenLogTransitionEvaluator.ResolveIssueFilters(mode, snapshot);
                OpenLogViewer(
                    snapshot.ProjectId,
                    snapshot.DisplayName,
                    logKind,
                    selectErrorsFilter,
                    selectWarningsFilter);
            }
        }

        autoOpenLogSession.ForgetInactive(
            snapshots.Where(s => s.IsActive).Select(s => s.ProjectId).ToList());
    }

    private void AutoShowStatusPanelWhileBuilding(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        var active = snapshots.Where(s => s.IsActive).ToList();
        foreach (var snapshot in active)
        {
            var enabled = currentSettings.Projects
                .FirstOrDefault(p => p.Id.Equals(snapshot.ProjectId, StringComparison.OrdinalIgnoreCase))
                ?.Local?.RunOptions.ShowStatusPanelWhileBuilding == true;
            if (!enabled)
            {
                continue;
            }

            previousProjectLifecycleState.TryGetValue(snapshot.ProjectId, out var previousState);
            if (!StatusPanelBuildVisibilityEvaluator.ShouldAutoShow(enabled, previousState, snapshot.State))
            {
                continue;
            }

            if (hoverPanel is not { IsVisible: true })
            {
                ShowStatusPanel();
                statusPanelAutoShownForBuild = true;
                MarkStatusPanelAutoPinned();
            }
        }

        var visibilityProjects = active.Select(snapshot =>
        {
            var enabled = currentSettings.Projects
                .FirstOrDefault(p => p.Id.Equals(snapshot.ProjectId, StringComparison.OrdinalIgnoreCase))
                ?.Local?.RunOptions.ShowStatusPanelWhileBuilding == true;
            return (ShowWhileBuildingEnabled: enabled == true, snapshot.State);
        });

        if (StatusPanelBuildVisibilityEvaluator.ShouldAutoHide(statusPanelAutoShownForBuild, visibilityProjects))
        {
            if (!statusPanelAutoShownForEditGating
                && !statusPanelPinnedAutoFlow
                && !StatusPanelBuildVisibilityEvaluator.ShouldKeepPanelVisibleUntilSiteReady(active))
            {
                HideAutoStatusPanel();
                statusPanelAutoShownForBuild = false;
            }
        }
    }

    private void MarkStatusPanelAutoPinned() => statusPanelPinnedAutoFlow = true;

    private IReadOnlyList<ProjectHealthSnapshot> ResolveStatusPanelSnapshots()
    {
        if (lastHealthSnapshots.Any(s => s.IsActive))
        {
            return lastHealthSnapshots;
        }

        var live = orchestrator?.GetHealthSnapshots();
        if (live is not null && live.Any(s => s.IsActive))
        {
            return live;
        }

        return lastHealthSnapshots;
    }

    private void UpdateStatusPanelIfVisible(IReadOnlyList<ProjectHealthSnapshot>? snapshots = null)
    {
        if (hoverPanel is not { IsVisible: true })
        {
            return;
        }

        var resolved = snapshots ?? ResolveStatusPanelSnapshots();
        if (!resolved.Any(s => s.IsActive))
        {
            HideAutoStatusPanel(suppressTrayHover: true);
            return;
        }

        hoverPanel.Update(resolved, statusPanelDismissAtUtc);
    }

    private void UpdateStatusPanelSiteReadyPin(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        var active = snapshots.Where(s => s.IsActive).ToList();
        var panelVisible = hoverPanel is { IsVisible: true };

        if (active.Count == 0)
        {
            CancelSiteReadyDismissSchedule();
            if (panelVisible)
            {
                HideAutoStatusPanel(suppressTrayHover: true);
            }

            return;
        }

        // Tray hover / manual open must stay up with no Closing countdown.
        // Site-ready auto-dismiss applies only to auto-pinned build/edit-gating flows.
        if (!statusPanelPinnedAutoFlow || !panelVisible || IsPointerEngagedWithStatusPanel())
        {
            var hadClosingCountdown = statusPanelDismissScheduled || statusPanelDismissAtUtc is not null;
            CancelSiteReadyDismissSchedule();
            if (hadClosingCountdown && panelVisible)
            {
                UpdateStatusPanelIfVisible(snapshots);
            }

            return;
        }

        if (active.Any(StatusPanelBuildVisibilityEvaluator.ShouldBlockSiteReadyDismiss))
        {
            CancelSiteReadyDismissSchedule();
            return;
        }

        if (!StatusPanelBuildVisibilityEvaluator.ShouldScheduleSiteReadyDismiss(active))
        {
            CancelSiteReadyDismissSchedule();
            return;
        }

        var hasListenUrl = active.Any(StatusPanelBuildVisibilityEvaluator.HasSiteLaunchConfigured);
        var siteReadyBanner = active.Any(StatusPanelBuildVisibilityEvaluator.ShouldShowSiteReady);
        var delay = hasListenUrl
            ? siteReadyBanner ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(8)
            : TimeSpan.FromSeconds(2);
        if (!statusPanelDismissScheduled)
        {
            ScheduleSiteReadyDismiss(delay, snapshots);
        }
    }

    /// <summary>
    /// Cursor is on the tray icon or the status panel — keep the panel open for watching (no auto-close).
    /// </summary>
    private bool IsPointerEngagedWithStatusPanel() =>
        pointerOverStatusPanel || IsCursorOverTrayIcon();

    private bool IsCursorOverTrayIcon() =>
        notifyIcon is not null
        && TrayIconShellInterop.TryGetIconScreenBounds(notifyIcon, out _)
        && TrayIconShellInterop.IsCursorOverIcon(notifyIcon);

    private void CancelSiteReadyDismissSchedule()
    {
        statusPanelDismissAtUtc = null;
        statusPanelDismissScheduled = false;
        siteReadyDismissTimer?.Stop();
    }

    private void ScheduleSiteReadyDismiss(TimeSpan delay, IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        statusPanelDismissAtUtc = DateTimeOffset.UtcNow.Add(delay);
        statusPanelDismissScheduled = true;

        siteReadyDismissTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        siteReadyDismissTimer.Tick -= SiteReadyDismissTick;
        siteReadyDismissTimer.Tick += SiteReadyDismissTick;
        siteReadyDismissTimer.Stop();
        siteReadyDismissTimer.Start();

        if (hoverPanel is { IsVisible: true })
        {
            UpdateStatusPanelIfVisible(snapshots);
        }
    }

    private void SiteReadyDismissTick(object? sender, EventArgs e)
    {
        if (statusPanelDismissAtUtc is not { } dismissAt)
        {
            siteReadyDismissTimer?.Stop();
            statusPanelDismissScheduled = false;
            return;
        }

        // Hovering the tray icon or panel means the user is watching — never auto-close.
        if (IsPointerEngagedWithStatusPanel())
        {
            CancelSiteReadyDismissSchedule();
            UpdateStatusPanelIfVisible();
            return;
        }

        if (DateTimeOffset.UtcNow < dismissAt)
        {
            UpdateStatusPanelIfVisible();
            return;
        }

        siteReadyDismissTimer?.Stop();
        statusPanelDismissAtUtc = null;
        statusPanelDismissScheduled = false;

        if (!statusPanelPinnedAutoFlow && hoverPanel is not { IsVisible: true })
        {
            return;
        }

        HideAutoStatusPanel(suppressTrayHover: true);
        statusPanelAutoShownForBuild = false;
        statusPanelAutoShownForEditGating = false;
    }

    private void AutoShowStatusPanelForEditGating(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        var suppressionEnabled = currentSettings.Monitor.DeferStartupBuildUntilQuiet
            || currentSettings.Monitor.CancelSupersededBuilds;
        if (!suppressionEnabled)
        {
            return;
        }

        var active = snapshots.Where(s => s.IsActive).ToList();
        var anyGatingActive = false;
        var anyBusyWork = false;

        foreach (var snapshot in active)
        {
            var showWhileBuilding = currentSettings.Projects
                .FirstOrDefault(p => p.Id.Equals(snapshot.ProjectId, StringComparison.OrdinalIgnoreCase))
                ?.Local?.RunOptions.ShowStatusPanelWhileBuilding == true;
            previousEditGatingActive.TryGetValue(snapshot.ProjectId, out var wasGating);
            previousProjectLifecycleState.TryGetValue(snapshot.ProjectId, out var previousState);
            var isBusy = StatusPanelBuildVisibilityEvaluator.IsBusyWorkState(snapshot.State);

            if (StatusPanelBuildVisibilityEvaluator.ShouldContinueThroughBuildFromEditGating(
                    showWhileBuilding,
                    previousState,
                    snapshot.State,
                    statusPanelAutoShownForEditGating && !statusPanelAutoShownForBuild))
            {
                // Keep the panel up from the quiet countdown into the build itself.
                if (hoverPanel is not { IsVisible: true })
                {
                    ShowStatusPanel();
                }

                statusPanelAutoShownForBuild = true;
                statusPanelAutoShownForEditGating = true;
                MarkStatusPanelAutoPinned();
            }
            else if (StatusPanelBuildVisibilityEvaluator.ShouldAutoShowForEditGating(
                    suppressionEnabled,
                    snapshot.IsEditGatingActive,
                    wasGating))
            {
                if (hoverPanel is not { IsVisible: true })
                {
                    ShowStatusPanel();
                }

                statusPanelAutoShownForEditGating = true;
            }
            else if (StatusPanelBuildVisibilityEvaluator.ShouldAutoShowForBusyWork(
                    suppressionEnabled,
                    showWhileBuilding,
                    previousState,
                    snapshot.State))
            {
                if (hoverPanel is not { IsVisible: true })
                {
                    ShowStatusPanel();
                }

                statusPanelAutoShownForEditGating = true;
                MarkStatusPanelAutoPinned();
            }

            if (snapshot.IsEditGatingActive)
            {
                anyGatingActive = true;
            }

            if (isBusy)
            {
                anyBusyWork = true;
            }

            previousEditGatingActive[snapshot.ProjectId] = snapshot.IsEditGatingActive;
        }

        if (statusPanelAutoShownForEditGating
            && !anyGatingActive
            && !anyBusyWork
            && !statusPanelAutoShownForBuild
            && !StatusPanelBuildVisibilityEvaluator.ShouldKeepPanelVisibleUntilSiteReady(active))
        {
            HideAutoStatusPanel();
            statusPanelAutoShownForEditGating = false;
        }
    }

    private void OnStatusPanelCloseRequested()
    {
        HideAutoStatusPanel(suppressTrayHover: true);
        statusPanelAutoShownForBuild = false;
        statusPanelAutoShownForEditGating = false;
    }

    private void HideAutoStatusPanel(bool suppressTrayHover = false)
    {
        CancelStatusPanelTimers();
        CancelSiteReadyDismissSchedule();
        statusPanelPinnedAutoFlow = false;
        StopStatusPanelPlacementTimer();
        if (suppressTrayHover)
        {
            trayHoverStatusPanelSuppressedUntil = DateTimeOffset.UtcNow.AddSeconds(5);
            trayHoverPollTimer?.Stop();
        }
        pointerOverStatusPanel = false;
        hoverPanel?.Hide();
        FlushStatusPanelLayout();
    }

    private static BuildLogKind? LogKindForAutoOpen(
        ProjectLifecycleState currentState,
        ProjectLifecycleState previousState)
    {
        if (previousState == ProjectLifecycleState.Testing
            || currentState is ProjectLifecycleState.Testing
                or ProjectLifecycleState.TestOk
                or ProjectLifecycleState.TestFailed)
        {
            return BuildLogKind.Test;
        }

        if (currentState is ProjectLifecycleState.Crashed
            && previousState is ProjectLifecycleState.Running or ProjectLifecycleState.Watching)
        {
            return BuildLogKind.Run;
        }

        return BuildLogKind.Build;
    }

    private void ApplyControlPlaneHost()
    {
        if (controlPlaneHost is null)
        {
            return;
        }

        try
        {
            controlPlaneHost.Apply(currentSettings.Monitor);
        }
        catch (Exception ex)
        {
            ToastNotificationService.ShowIfEnabled(
                "Control plane failed to start",
                $"Could not bind http://127.0.0.1:{currentSettings.Monitor.ControlPlanePort}/ — {ex.Message}",
                ToastKind.Warning,
                UserNotificationCategory.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        displayChangeWatcher?.Dispose();
        displayChangeWatcher = null;
        buildIconAnimationTimer?.Stop();
        buildIconAnimationTimer = null;
        dispatcherHealthProbe?.Dispose();
        dispatcherHealthProbe = null;
        controlPlaneHost?.Dispose();
        controlPlaneHost = null;
        orchestrator?.Dispose();
        orchestrator = null;

        if (notifyIcon is not null)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            notifyIcon = null;
        }

        trayContextMenu?.Dispose();
        trayContextMenu = null;

        base.OnExit(e);
    }

    private Forms.NotifyIcon BuildNotifyIcon()
    {
        var icon = new Forms.NotifyIcon
        {
            Text = string.Empty,
            Icon = TrafficLightIconFactory.GetIcon(MonitorHealth.Unknown)
        };

        trayContextMenu = new Forms.ContextMenuStrip();
        RebuildTrayMenu();

        trayContextMenu.Opening += (_, _) =>
        {
            Volatile.Write(ref trayMenuOpen, 1);
            orchestrator?.SetTrayMenuOpen(true);
            CancelStatusPanelTimers();
            hoverPanel?.Hide();
            TrayScreenPlacement.CaptureFromCursor();
            try
            {
                RebuildTrayMenu();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tray menu refresh failed: {ex}");
            }
        };

        trayContextMenu.Closed += (_, _) =>
        {
            Volatile.Write(ref trayMenuOpen, 0);
            orchestrator?.SetTrayMenuOpen(false);
        };

        TrayMenuTheme.Apply(trayContextMenu, ThemeService.CurrentResolved);
        icon.ContextMenuStrip = trayContextMenu;

        icon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Right)
            {
                CancelStatusPanelTimers();
                hoverPanel?.Hide();
                return;
            }

            if (args.Button == Forms.MouseButtons.Left)
            {
                TrayScreenPlacement.CaptureFromCursor();
                ToggleStatusPanel();
            }
        };

        icon.MouseMove += (_, _) => OnNotifyIconMouseMove();

        return icon;
    }

    private void OnNotifyIconMouseMove()
    {
        if (!CanShowStatusPanelOnTrayHover())
        {
            ScheduleHideStatusPanel();
            return;
        }

        // Hover watch: cancel any Closing countdown so the panel stays up while on the icon.
        CancelSiteReadyDismissSchedule();
        hideStatusPanelTimer?.Stop();
        ShowStatusPanel();
        EnsureTrayHoverPollTimer();
    }

    private bool CanShowStatusPanelOnTrayHover() =>
        Volatile.Read(ref trayMenuOpen) == 0
        && DateTimeOffset.UtcNow >= trayHoverStatusPanelSuppressedUntil;

    private void EnsureTrayHoverPollTimer()
    {
        trayHoverPollTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        trayHoverPollTimer.Tick -= TrayHoverPollTick;
        trayHoverPollTimer.Tick += TrayHoverPollTick;
        if (!trayHoverPollTimer.IsEnabled)
        {
            trayHoverPollTimer.Start();
        }
    }

    private void TrayHoverPollTick(object? sender, EventArgs e)
    {
        if (!CanShowStatusPanelOnTrayHover())
        {
            trayHoverPollTimer?.Stop();
            hoverPanel?.Hide();
            return;
        }

        if (notifyIcon is null)
        {
            trayHoverPollTimer?.Stop();
            hoverPanel?.Hide();
            return;
        }

        if (!IsCursorOverTrayIcon()
            && !pointerOverStatusPanel
            && !statusPanelPinnedAutoFlow)
        {
            ScheduleHideStatusPanel();
            trayHoverPollTimer?.Stop();
            return;
        }

        if (IsPointerEngagedWithStatusPanel())
        {
            CancelSiteReadyDismissSchedule();
            hideStatusPanelTimer?.Stop();
        }

        if (hoverPanel is { IsVisible: true })
        {
            RepositionStatusPanelNearTray();
            UpdateStatusPanelIfVisible();
        }
    }

    private void RefreshStatusPanelIfVisible()
    {
        if (hoverPanel is not { IsVisible: true })
        {
            return;
        }

        if (!IsCursorOverTrayIcon()
            && !pointerOverStatusPanel
            && !statusPanelPinnedAutoFlow)
        {
            ScheduleHideStatusPanel();
            return;
        }

        if (IsPointerEngagedWithStatusPanel())
        {
            CancelSiteReadyDismissSchedule();
            hideStatusPanelTimer?.Stop();
        }

        RepositionStatusPanelNearTray();
        UpdateStatusPanelIfVisible();
    }

    private void ToggleStatusPanel()
    {
        CancelStatusPanelTimers();
        EnsureHoverPanel();

        if (hoverPanel is { IsVisible: true })
        {
            HideAutoStatusPanel();
            statusPanelAutoShownForBuild = false;
            statusPanelAutoShownForEditGating = false;
            return;
        }

        hoverPanel!.Update(ResolveStatusPanelSnapshots(), statusPanelDismissAtUtc);
        ShowStatusPanelNearTray();
    }

    private void ShowStatusPanelNearTray()
    {
        if (hoverPanel is null)
        {
            return;
        }

        GetTrayPlacementContext(out var trayIconBounds, out var trayWindowHandle);
        hoverPanel.ShowNearTray(trayIconBounds, trayWindowHandle);
        EnsureStatusPanelPlacementTimer();
    }

    private void GetTrayPlacementContext(out Rectangle? trayIconBounds, out IntPtr trayWindowHandle)
    {
        trayIconBounds = null;
        trayWindowHandle = IntPtr.Zero;
        if (notifyIcon is null)
        {
            return;
        }

        if (TrayIconShellInterop.TryGetIconScreenBounds(notifyIcon, out var bounds))
        {
            trayIconBounds = bounds;
        }

        TrayIconShellInterop.TryGetNotifyIconWindowHandle(notifyIcon, out trayWindowHandle);
    }

    private void RepositionStatusPanelNearTray()
    {
        if (hoverPanel is not { IsVisible: true })
        {
            return;
        }

        GetTrayPlacementContext(out var trayIconBounds, out var trayWindowHandle);
        hoverPanel.FollowTray(trayIconBounds, trayWindowHandle);
    }

    private void EnsureStatusPanelPlacementTimer()
    {
        statusPanelPlacementTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        statusPanelPlacementTimer.Tick -= StatusPanelPlacementTick;
        statusPanelPlacementTimer.Tick += StatusPanelPlacementTick;
        if (!statusPanelPlacementTimer.IsEnabled)
        {
            statusPanelPlacementTimer.Start();
        }
    }

    private void StopStatusPanelPlacementTimer() => statusPanelPlacementTimer?.Stop();

    private void StatusPanelPlacementTick(object? sender, EventArgs e)
    {
        if (hoverPanel is not { IsVisible: true })
        {
            StopStatusPanelPlacementTimer();
            return;
        }

        RepositionStatusPanelNearTray();
    }

    private void ShowStatusPanel()
    {
        var snapshots = ResolveStatusPanelSnapshots();
        if (!snapshots.Any(s => s.IsActive))
        {
            return;
        }

        CancelStatusPanelTimers();
        EnsureHoverPanel();
        hoverPanel!.Update(snapshots, statusPanelDismissAtUtc);
        ShowStatusPanelNearTray();
    }

    private void CancelStatusPanelTimers() => hideStatusPanelTimer?.Stop();

    private void ScheduleHideStatusPanel()
    {
        if (statusPanelPinnedAutoFlow)
        {
            return;
        }

        pointerOverStatusPanel = false;
        hideStatusPanelTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        hideStatusPanelTimer.Tick -= HideStatusPanelTick;
        hideStatusPanelTimer.Tick += HideStatusPanelTick;
        hideStatusPanelTimer.Stop();
        hideStatusPanelTimer.Start();
    }

    private void HideStatusPanelTick(object? sender, EventArgs e)
    {
        hideStatusPanelTimer?.Stop();
        if (statusPanelPinnedAutoFlow || IsPointerEngagedWithStatusPanel())
        {
            return;
        }

        hoverPanel?.Hide();
        FlushStatusPanelLayout();
    }

    private void EnsureHoverPanel()
    {
        if (hoverPanel is not null)
        {
            return;
        }

        if (windowsLayoutStore is null)
        {
            return;
        }

        hoverPanel = new HoverStatusPanel
        {
            FollowVirtualDesktop = currentSettings.AppBehavior.FollowStatusPanelToVirtualDesktop
        };
        hoverPanel.ApplyLayout(windowsLayoutStore.Layout.StatusPanel);
        hoverPanel.SizeChanged += (_, _) => ScheduleSaveStatusPanelLayout();
        ApplyThemeToUi();
        hoverPanel.ViewLogRequested += projectId => OpenLogViewerForProject(projectId);
        hoverPanel.CopyErrorsRequested += projectId =>
            RunTrayMenuBackgroundAction(() => CopyProjectErrorsAsync(projectId));
        hoverPanel.RestartAppRequested += projectId =>
            RunTrayMenuBackgroundAction(() => orchestrator!.RestartAppAsync(projectId, CancellationToken.None));
        hoverPanel.RebuildAndRestartRequested += projectId =>
        {
            CancelSiteReadyDismissSchedule();
            statusPanelAutoShownForBuild = true;
            hoverPanel?.PrepareForPendingRebuild();
            RunTrayMenuBackgroundAction(() => orchestrator!.RebuildAndRestartAsync(projectId, CancellationToken.None));
        };
        hoverPanel.RunTestsRequested += projectId =>
        {
            var name = currentSettings.Projects.FirstOrDefault(p => p.Id == projectId)?.DisplayName ?? projectId;
            OpenLogViewer(projectId, name, BuildLogKind.Test);
            _ = Task.Run(async () =>
            {
                try
                {
                    await orchestrator!.RunTestsAsync(projectId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                        ToastNotificationService.ShowIfEnabled(
                            "Local Build Monitor",
                            ToastNotificationService.FormatException(ex),
                            ToastKind.Error,
                            UserNotificationCategory.Error));
                }
            });
        };
        hoverPanel.MarkStillEditingRequested += projectId =>
            RunTrayMenuBackgroundAction(async () =>
            {
                var result = orchestrator!.HandleStillEditingClick(projectId);
                if (result == StillEditingClickResult.NotApplicable)
                {
                    return;
                }

                var message = result switch
                {
                    StillEditingClickResult.QuietPeriodExtended =>
                        "Rebuild wait extended — AI still working.",
                    StillEditingClickResult.BuildMarkedUnexpected =>
                        "Current build marked unexpected in Build diagnostics.",
                    _ => string.Empty
                };

                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                    ToastNotificationService.ShowIfEnabled(
                        "Local Build Monitor",
                        message,
                        ToastKind.Info,
                        UserNotificationCategory.Info));
            });
        hoverPanel.CloseRequested += OnStatusPanelCloseRequested;
        hoverPanel.IsVisibleChanged += (_, _) =>
        {
            if (hoverPanel.IsVisible)
            {
                EnsureStatusPanelPlacementTimer();
            }
            else
            {
                StopStatusPanelPlacementTimer();
            }
        };
        hoverPanel.MouseEnter += (_, _) =>
        {
            pointerOverStatusPanel = true;
            hideStatusPanelTimer?.Stop();
            CancelSiteReadyDismissSchedule();
            UpdateStatusPanelIfVisible();
        };
        hoverPanel.MouseLeave += (_, _) =>
        {
            pointerOverStatusPanel = false;
            if (statusPanelPinnedAutoFlow && !IsCursorOverTrayIcon())
            {
                UpdateStatusPanelSiteReadyPin(ResolveStatusPanelSnapshots());
            }

            ScheduleHideStatusPanel();
        };
        hoverPanel.Deactivated += (_, _) =>
        {
            if (!IsPointerEngagedWithStatusPanel())
            {
                ScheduleHideStatusPanel();
            }
        };
        hoverPanel.Closed += (_, _) => hoverPanel = null;
    }

    private static BuildLogKind? LogKindForFailure(ProjectLifecycleState state) =>
        state switch
        {
            ProjectLifecycleState.Crashed => BuildLogKind.Run,
            ProjectLifecycleState.BuildFailed => BuildLogKind.Build,
            ProjectLifecycleState.TestFailed => BuildLogKind.Test,
            _ => null
        };

    private ProjectHealthSnapshot? FindSnapshot(string projectId) =>
        orchestrator?.GetHealthSnapshots().FirstOrDefault(s => s.ProjectId == projectId);

    private void OpenLogViewerForProject(string projectId, string? displayName = null)
    {
        var snapshot = FindSnapshot(projectId);
        OpenLogViewer(
            projectId,
            displayName,
            snapshot is not null
                ? LogErrorExporter.ResolvePrimaryLogKind(snapshot.State, snapshot.IssueCountsText)
                : null,
            selectErrorsFilter: snapshot is { ErrorCount: > 0 });
    }

    private async Task CopyProjectErrorsAsync(string projectId)
    {
        if (orchestrator is null)
        {
            return;
        }

        var snapshot = FindSnapshot(projectId);
        if (snapshot is null || snapshot.ErrorCount == 0)
        {
            return;
        }

        var kind = LogErrorExporter.ResolvePrimaryLogKind(snapshot.State, snapshot.IssueCountsText);
        var live = orchestrator.GetLiveBuildLog(projectId, kind);
        IReadOnlyList<string> errors;
        if (live is not null)
        {
            errors = LogErrorExporter.GetErrorLines(kind, live.Text);
        }
        else
        {
            var metadata = await orchestrator.LogStore.LoadMetadataAsync(projectId, kind).ConfigureAwait(false);
            errors = metadata?.ErrorLines ?? [];
            if (errors.Count == 0 && metadata is not null)
            {
                var logText = await orchestrator.LogStore
                    .LoadLogTextAsync(metadata, currentSettings.Monitor.MaxLogDisplayBytes)
                    .ConfigureAwait(false);
                errors = LogErrorExporter.GetErrorLines(kind, logText);
            }
        }

        if (errors.Count == 0 && !string.IsNullOrWhiteSpace(snapshot.LastErrorPreview))
        {
            errors = [snapshot.LastErrorPreview];
        }

        if (errors.Count == 0)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, errors)));
    }

    private void ScheduleSaveStatusPanelLayout()
    {
        if (hoverPanel is null || windowsLayoutStore is null || !hoverPanel.IsLoaded)
        {
            return;
        }

        statusPanelLayoutSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        statusPanelLayoutSaveTimer.Tick -= StatusPanelLayoutSaveTick;
        statusPanelLayoutSaveTimer.Tick += StatusPanelLayoutSaveTick;
        statusPanelLayoutSaveTimer.Stop();
        statusPanelLayoutSaveTimer.Start();
    }

    private void StatusPanelLayoutSaveTick(object? sender, EventArgs e)
    {
        statusPanelLayoutSaveTimer?.Stop();
        FlushStatusPanelLayout();
    }

    private void FlushStatusPanelLayout()
    {
        if (hoverPanel is null || windowsLayoutStore is null || !hoverPanel.IsLoaded)
        {
            return;
        }

        if (!double.IsFinite(hoverPanel.ActualWidth) || hoverPanel.ActualWidth < hoverPanel.MinWidth
            || !double.IsFinite(hoverPanel.ActualHeight) || hoverPanel.ActualHeight < hoverPanel.MinHeight)
        {
            return;
        }

        hoverPanel.CaptureLayout(windowsLayoutStore.Layout.StatusPanel);
        _ = windowsLayoutStore.SaveAsync();
    }

    private void OpenLogViewer(
        string projectId,
        string? displayName = null,
        BuildLogKind? logKind = null,
        bool selectErrorsFilter = false,
        bool selectWarningsFilter = false)
    {
        if (orchestrator is null)
        {
            return;
        }

        var name = displayName ?? currentSettings.Projects.FirstOrDefault(p => p.Id == projectId)?.DisplayName ?? projectId;
        if (windowsLayoutStore is null)
        {
            return;
        }

        if (openLogViewers.TryGetValue(projectId, out var existing)
            && LogViewerWindowReuse.ShouldActivateExisting(hasOpenEntry: true, windowIsLoaded: existing.IsLoaded))
        {
            try
            {
                existing.ConfigureVirtualDesktopFollow(
                    currentSettings.AppBehavior.FollowBuildLogToVirtualDesktop);
                WindowLayoutService.Apply(existing, windowsLayoutStore.Layout.BuildLog, 960, 720);
                if (double.IsNaN(windowsLayoutStore.Layout.BuildLog.Left))
                {
                    TrayScreenPlacement.PlaceWindowCentered(existing);
                }

                if (!existing.IsVisible)
                {
                    existing.Show();
                }

                if (logKind is not null)
                {
                    existing.SelectLogKind(logKind.Value);
                }

                if (selectErrorsFilter)
                {
                    existing.SelectErrorsFilter();
                }
                else if (selectWarningsFilter)
                {
                    existing.SelectWarningsFilter();
                }

                existing.Activate();
                existing.Focus();
                existing.TryFollowVirtualDesktop();
                return;
            }
            catch (InvalidOperationException)
            {
                openLogViewers.Remove(projectId);
            }
        }

        var viewer = new BuildLogViewerWindow(
            orchestrator.LogStore,
            windowsLayoutStore,
            projectId,
            name,
            currentSettings.Monitor.MaxLogDisplayBytes,
            orchestrator.GetLiveBuildLog);
        viewer.ConfigureVirtualDesktopFollow(currentSettings.AppBehavior.FollowBuildLogToVirtualDesktop);
        viewer.Closed += (_, _) => openLogViewers.Remove(projectId);
        openLogViewers[projectId] = viewer;
        viewer.Show();
        viewer.TryFollowVirtualDesktop();

        if (logKind is not null)
        {
            viewer.SelectLogKind(logKind.Value);
        }

        if (selectErrorsFilter)
        {
            viewer.SelectErrorsFilter();
        }
        else if (selectWarningsFilter)
        {
            viewer.SelectWarningsFilter();
        }
    }

    private void ShowBuildDiagnostics()
    {
        if (orchestrator is null)
        {
            return;
        }

        if (diagnosticsWindow is { IsLoaded: true })
        {
            WindowLayoutService.Apply(diagnosticsWindow, windowsLayoutStore!.Layout.Diagnostics, 1100, 640);
            if (double.IsNaN(windowsLayoutStore.Layout.Diagnostics.Left))
            {
                TrayScreenPlacement.PlaceWindowCentered(diagnosticsWindow);
            }

            if (!diagnosticsWindow.IsVisible)
            {
                diagnosticsWindow.Show();
            }

            diagnosticsWindow.Activate();
            return;
        }

        diagnosticsWindow = new BuildDiagnosticsWindow(orchestrator.TriggerJournal, orchestrator, windowsLayoutStore!);
        diagnosticsWindow.Closed += (_, _) => diagnosticsWindow = null;
        diagnosticsWindow.Show();
    }

    private void ShowBuildMonitorHealth()
    {
        if (windowsLayoutStore is null)
        {
            return;
        }

        if (buildMonitorHealthWindow is { IsLoaded: true })
        {
            WindowLayoutService.Apply(buildMonitorHealthWindow, windowsLayoutStore.Layout.BuildMonitorHealth, 980, 520);
            if (double.IsNaN(windowsLayoutStore.Layout.BuildMonitorHealth.Left))
            {
                TrayScreenPlacement.PlaceWindowCentered(buildMonitorHealthWindow);
            }

            if (!buildMonitorHealthWindow.IsVisible)
            {
                buildMonitorHealthWindow.Show();
            }

            buildMonitorHealthWindow.Activate();
            return;
        }

        buildMonitorHealthWindow = new BuildMonitorHealthWindow(windowsLayoutStore, orchestrator!);
        buildMonitorHealthWindow.Closed += (_, _) => buildMonitorHealthWindow = null;
        buildMonitorHealthWindow.Show();
    }

    private async Task ShowSettingsAsync()
    {
        if (settingsStore is null || windowsLayoutStore is null)
        {
            return;
        }

        var window = new SettingsWindow(CloneSettings(currentSettings), windowsLayoutStore);
        if (double.IsNaN(windowsLayoutStore.Layout.Settings.Left))
        {
            TrayScreenPlacement.PlaceWindowCentered(window);
        }

        var saved = window.ShowDialog() == true;
        if (Volatile.Read(ref exitRequested) != 0)
        {
            return;
        }

        if (!saved)
        {
            return;
        }

        try
        {
            await settingsStore.SaveAsync(window.Settings);
        }
        catch (Exception ex)
        {
            ToastNotificationService.ShowIfEnabled(
                "Could not save settings",
                ToastNotificationService.FormatException(ex),
                ToastKind.Error,
                UserNotificationCategory.Error);
            return;
        }

        var previousSettings = currentSettings;
        currentSettings = window.Settings;
        ThemeService.ApplyTheme(currentSettings.AppBehavior.Theme);
        ToastNotificationService.ApplySettings(currentSettings.AppBehavior);
        WindowsStartupService.Apply(currentSettings.AppBehavior.RunOnLogon);
        ApplyVirtualDesktopWindowSettings();
        ApplyThemeToUi();
        RebuildTrayMenu();

        var plan = SettingsApplyImpactClassifier.CreatePlan(previousSettings, currentSettings);
        if (plan.ColdStartActiveProjectsWithBuild
            || plan.RemountAffectedLocalProjectsWithoutBuild
            || plan.ApplyOrchestratorSettings)
        {
            var applyVersion = Interlocked.Increment(ref settingsApplyVersion);
            _ = ApplySettingsAndStartInBackgroundAsync(applyVersion, plan);
        }

        if (plan.ShowProjectsStartingToast)
        {
            ToastNotificationService.ShowIfEnabled(
                "Settings saved",
                "Projects with start on launch enabled are starting in the background.",
                ToastKind.Success,
                UserNotificationCategory.Info);
        }
        else
        {
            ToastNotificationService.ShowIfEnabled(
                "Settings saved",
                plan.Impact switch
                {
                    SettingsApplyImpact.None => "No changes to apply.",
                    SettingsApplyImpact.Presentation => "Presentation settings updated.",
                    SettingsApplyImpact.SoftRuntime => "Runtime settings updated without rebuilding.",
                    SettingsApplyImpact.HardRestart =>
                        "Local runtime remounted without rebuilding.",
                    _ => "Settings updated."
                },
                ToastKind.Success,
                UserNotificationCategory.Info);
        }
    }

    private async Task ApplySettingsAndStartInBackgroundAsync(int applyVersion, SettingsApplyPlan plan)
    {
        try
        {
            await Task.Run(async () => await ApplySettingsAndStartAsync(plan).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (applyVersion == settingsApplyVersion)
            {
                await Dispatcher.InvokeAsync(() =>
                    ToastNotificationService.ShowIfEnabled(
                        "Failed to apply settings",
                        ToastNotificationService.FormatException(ex),
                        ToastKind.Error,
                        UserNotificationCategory.Error));
            }
        }
    }

    private void RequestExit()
    {
        switch (AppQuitLifecycle.TryClaim(ref exitRequested))
        {
            case AppQuitClaimResult.AlreadyInProgress:
                // Prior Exit/quit accepted but process stayed alive (e.g. hung child stop).
                Environment.Exit(0);
                return;
            case AppQuitClaimResult.Accepted:
                break;
        }

        // Failsafe BEFORE any UI work: /app/quit runs on an HTTP thread and must not
        // throw (or skip the deadline) when touching WinForms/WPF objects.
        AppQuitLifecycle.ArmFailsafeThenScheduleGraceful(
            ArmExitFailsafe,
            ScheduleGracefulExitOnUiThread);
    }

    private static void ArmExitFailsafe()
    {
        // Hard deadline so /app/quit and tray Exit cannot leave a zombie holding binaries.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            Environment.Exit(0);
        });
    }

    private void ScheduleGracefulExitOnUiThread()
    {
        void beginUiTeardown()
        {
            try
            {
                CancelStatusPanelTimers();
                hoverPanel?.Hide();

                if (notifyIcon is not null)
                {
                    notifyIcon.Visible = false;
                }

                if (trayContextMenu is not null)
                {
                    trayContextMenu.Hide();
                }

                // Normal (not ApplicationIdle): Idle can be delayed while watch/UI work is busy.
                // Defer one pump so WinForms tray menu can finish its click handler.
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _ = ExitAsync());
            }
            catch (Exception ex)
            {
                // Failsafe already armed — do not rethrow onto the HTTP /app/quit thread.
                System.Diagnostics.Debug.WriteLine($"Graceful exit UI schedule failed: {ex}");
            }
        }

        if (Dispatcher.CheckAccess())
        {
            beginUiTeardown();
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, beginUiTeardown);
        }
    }

    private async Task ExitAsync()
    {
        try
        {
            if (orchestrator is not null)
            {
                orchestrator.HealthUpdated -= OnHealthUpdated;
                orchestrator.UserNotification -= OnUserNotification;
            }

            ThemeService.ThemeChanged -= OnThemeChanged;
            buildIconAnimationTimer?.Stop();
            buildIconAnimationTimer = null;

            ToastNotificationService.CloseAll();

            foreach (var viewer in openLogViewers.Values.ToList())
            {
                try
                {
                    viewer.Close();
                }
                catch
                {
                    // ignore during shutdown
                }
            }

            openLogViewers.Clear();
            diagnosticsWindow?.Close();
            diagnosticsWindow = null;
            buildMonitorHealthWindow?.Close();
            buildMonitorHealthWindow = null;
            dispatcherHealthProbe?.Dispose();
            dispatcherHealthProbe = null;
            WorkerHealthRegistry.Shared.Unregister("ui.health-callback");
            hoverPanel?.Close();
            hoverPanel = null;

            // Settings / wizards use ShowDialog — close them so Shutdown is not blocked.
            foreach (Window window in Windows.Cast<Window>().ToList())
            {
                try
                {
                    window.Close();
                }
                catch
                {
                    // ignore during shutdown
                }
            }

            if (orchestrator is not null)
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try
                {
                    await Task.Run(async () =>
                        await orchestrator.StopAllAsync().ConfigureAwait(false))
                        .WaitAsync(stopTimeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // proceed with exit even if child processes are still stopping
                }

                controlPlaneHost?.Dispose();
                controlPlaneHost = null;
                orchestrator.Dispose();
                orchestrator = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exit cleanup failed: {ex}");
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (notifyIcon is not null)
                {
                    notifyIcon.ContextMenuStrip = null;
                    notifyIcon.Dispose();
                    notifyIcon = null;
                }

                trayContextMenu?.Dispose();
                trayContextMenu = null;

                Shutdown();
            });
        }
    }

    private void RunTrayMenuUiAction(Action action)
    {
        CloseTrayMenu();
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ToastNotificationService.ShowIfEnabled(
                "Local Build Monitor",
                ToastNotificationService.FormatException(ex),
                ToastKind.Error,
                UserNotificationCategory.Error);
        }
    }

    private void RunTrayMenuBackgroundAction(Func<Task> action)
    {
        CloseTrayMenu();
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                    ToastNotificationService.ShowIfEnabled(
                        "Local Build Monitor",
                        ToastNotificationService.FormatException(ex),
                        ToastKind.Error,
                        UserNotificationCategory.Error));
            }
        });
    }

    private void CloseTrayMenu()
    {
        if (trayContextMenu is null)
        {
            return;
        }

        trayContextMenu.Close(Forms.ToolStripDropDownCloseReason.ItemClicked);
    }

    private void RebuildTrayMenu()
    {
        if (trayContextMenu is null || orchestrator is null)
        {
            return;
        }

        trayMenuBuilder.Rebuild(trayContextMenu, currentSettings, orchestrator, TrayMenuHost);
        ApplyTrayMenuTheme();
    }

    private TrayContextMenuBuilder.Host TrayMenuHost => new()
    {
        RunUi = RunTrayMenuUiAction,
        RunBackground = RunTrayMenuBackgroundAction,
        ShowStatus = ShowStatusPanel,
        ShowBuildDiagnostics = ShowBuildDiagnostics,
        ShowBuildMonitorHealth = ShowBuildMonitorHealth,
        ShowSettings = () => _ = ShowSettingsAsync(),
        RequestExit = RequestExit,
        OpenLogViewerForProject = OpenLogViewerForProject,
        StartRunTestsForProjects = StartRunTestsForProjects,
        InstallControlPlaneAgentSkill = InstallControlPlaneAgentSkill
    };

    private void InstallControlPlaneAgentSkill(string projectRootFolder, string displayName)
    {
        var result = ControlPlaneAgentSkillInstaller.Install(projectRootFolder);
        if (result.Ok)
        {
            System.Windows.MessageBox.Show(
                $"Installed Cursor agent integration for {displayName}:\n\n"
                + $"Skill: {result.DestinationPath}\n"
                + $"Always-on rule: {result.RuleDestinationPath}\n\n"
                + "New agent chats in that workspace use BuildMonitor automatically — no paste required.",
                "Control plane skill",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        System.Windows.MessageBox.Show(
            result.Error ?? "Install failed.",
            "Control plane skill",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ApplyTrayMenuTheme()
    {
        if (trayContextMenu is null)
        {
            return;
        }

        TrayMenuTheme.Apply(trayContextMenu, ThemeService.Resolve(currentSettings.AppBehavior.Theme));
    }

    private void StartRunTestsForProjects(IReadOnlyList<MonitoredProjectSettings> projects)
    {
        foreach (var project in projects)
        {
            OpenLogViewer(project.Id, project.DisplayName, BuildLogKind.Test);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var project in projects)
                {
                    await orchestrator!.RunTestsAsync(project.Id, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                    ToastNotificationService.ShowIfEnabled(
                        "Local Build Monitor",
                        ToastNotificationService.FormatException(ex),
                        ToastKind.Error,
                        UserNotificationCategory.Error));
            }
        });
    }

    private async Task RebuildAllActiveAsync()
    {
        foreach (var project in currentSettings.Projects.Where(p => p.IsActiveInSession))
        {
            await orchestrator!.RebuildAsync(project.Id, CancellationToken.None);
        }
    }

    private void PlayBuildNotificationSounds(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots.Where(s => s.IsActive))
        {
            previousProjectHealth.TryGetValue(snapshot.ProjectId, out var previous);

            if (previous != snapshot.Health)
            {
                if (snapshot.Health == MonitorHealth.Red
                    && previous == MonitorHealth.Amber
                    && currentSettings.Monitor.PlaySoundOnBuildError)
                {
                    BuildNotificationSoundService.PlayBuildFailed();
                }
                else if (snapshot.Health == MonitorHealth.Green
                         && previous == MonitorHealth.Amber
                         && currentSettings.Monitor.PlaySoundOnBuildSuccess)
                {
                    BuildNotificationSoundService.PlayBuildSucceeded();
                }
            }

            previousProjectHealth[snapshot.ProjectId] = snapshot.Health;
            previousProjectLifecycleState[snapshot.ProjectId] = snapshot.State;
        }

        var activeIds = snapshots.Where(s => s.IsActive).Select(s => s.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in previousProjectHealth.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            previousProjectHealth.Remove(staleId);
            previousProjectLifecycleState.Remove(staleId);
        }
    }

    private static AppSettings CloneSettings(AppSettings source) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(source)) ?? new AppSettings();

    private void ApplyVirtualDesktopWindowSettings()
    {
        if (hoverPanel is not null)
        {
            hoverPanel.FollowVirtualDesktop = currentSettings.AppBehavior.FollowStatusPanelToVirtualDesktop;
        }

        var followLog = currentSettings.AppBehavior.FollowBuildLogToVirtualDesktop;
        foreach (var viewer in openLogViewers.Values)
        {
            viewer.ConfigureVirtualDesktopFollow(followLog);
        }
    }

    private void ApplyThemeToUi()
    {
        var theme = ThemeService.Resolve(currentSettings.AppBehavior.Theme);
        hoverPanel?.ApplyTheme(theme);
        ApplyThemeToDiagnosticsWindow(theme);
        ApplyThemeToBuildMonitorHealthWindow(theme);
        ApplyTrayMenuTheme();
    }

    private void OnThemeChanged(ResolvedTheme theme) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            hoverPanel?.ApplyTheme(theme);
            ApplyThemeToDiagnosticsWindow(theme);
            ApplyThemeToBuildMonitorHealthWindow(theme);
            ApplyTrayMenuTheme();
        });

    private void ApplyThemeToDiagnosticsWindow(ResolvedTheme theme)
    {
        if (diagnosticsWindow is not { IsLoaded: true })
        {
            return;
        }

        ThemeService.ApplyToWindow(diagnosticsWindow, theme);
        ThemeService.ApplyChrome(diagnosticsWindow, theme == ResolvedTheme.Dark);
    }

    private void ApplyThemeToBuildMonitorHealthWindow(ResolvedTheme theme)
    {
        if (buildMonitorHealthWindow is not { IsLoaded: true })
        {
            return;
        }

        ThemeService.ApplyToWindow(buildMonitorHealthWindow, theme);
        ThemeService.ApplyChrome(buildMonitorHealthWindow, theme == ResolvedTheme.Dark);
    }

    private void UpdateTrayIcon(MonitorHealth health, bool isBuilding, bool webReady)
    {
        if (notifyIcon is null)
        {
            return;
        }

        currentTrayHealth = health;
        currentTrayBuilding = isBuilding;
        currentTrayWebReady = webReady;

        if (isBuilding)
        {
            EnsureBuildIconAnimationTimer();
        }
        else
        {
            StopBuildIconAnimationTimer();
        }

        ApplyTrayIconFrame();
    }

    private void EnsureBuildIconAnimationTimer()
    {
        buildIconAnimationTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        buildIconAnimationTimer.Tick -= BuildIconAnimationTick;
        buildIconAnimationTimer.Tick += BuildIconAnimationTick;

        if (!buildIconAnimationTimer.IsEnabled)
        {
            buildIconAnimationFrame = 0;
            buildIconAnimationTimer.Start();
        }
    }

    private void StopBuildIconAnimationTimer()
    {
        if (buildIconAnimationTimer is null)
        {
            return;
        }

        buildIconAnimationTimer.Stop();
        buildIconAnimationTimer.Tick -= BuildIconAnimationTick;
        buildIconAnimationFrame = 0;
    }

    private void BuildIconAnimationTick(object? sender, EventArgs e)
    {
        buildIconAnimationFrame = (buildIconAnimationFrame + 1) % 4;
        ApplyTrayIconFrame();
    }

    private void ApplyTrayIconFrame()
    {
        if (notifyIcon is null)
        {
            return;
        }

        notifyIcon.Icon = TrafficLightIconFactory.GetIcon(
            currentTrayHealth,
            currentTrayBuilding,
            buildIconAnimationFrame,
            currentTrayWebReady);
        notifyIcon.Text = string.Empty;

        if (hoverPanel is { IsVisible: true })
        {
            RefreshStatusPanelIfVisible();
        }
    }
}
