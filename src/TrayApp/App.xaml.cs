using System.IO;
using System.Windows;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
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
    private BuildDiagnosticsWindow? diagnosticsWindow;
    private BuildMonitorHealthWindow? buildMonitorHealthWindow;
    private DispatcherHealthProbe? dispatcherHealthProbe;
    private HoverStatusPanel? hoverPanel;
    private SettingsStore? settingsStore;
    private AppWindowsLayoutStore? windowsLayoutStore;
    private AppSettings currentSettings = new();
    private string appDataDirectory = string.Empty;
    private DispatcherTimer? hideStatusPanelTimer;
    private DispatcherTimer? statusPanelLayoutSaveTimer;
    private bool pointerOverStatusPanel;
    private readonly Dictionary<string, MonitorHealth> previousProjectHealth = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProjectLifecycleState> previousProjectState = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildLogViewerWindow> openLogViewers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> autoOpenedLogForFailure = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> fileChangeBuildStarts = new(StringComparer.OrdinalIgnoreCase);
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
    private MonitorHealth pendingHealthRollup = MonitorHealth.Unknown;
    private int healthUiUpdateScheduled;
    private int trayMenuOpen;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BuildMonitor");
        MigrateLegacyAppDataIfNeeded(appDataDirectory);
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
        orchestrator.HealthUpdated += OnHealthUpdated;
        orchestrator.UserNotification += OnUserNotification;

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

        if (ShouldAutoOpenBuildMonitorHealth())
        {
            await OpenBuildMonitorHealthWhenReadyAsync();
            await WaitForUiIdleAsync();
        }

        await Task.Run(async () => await ApplySettingsAndStartAsync().ConfigureAwait(false));
    }

    private async Task OpenBuildMonitorHealthWhenReadyAsync()
    {
        await Dispatcher.InvokeAsync(ShowBuildMonitorHealth, DispatcherPriority.Loaded);
        if (buildMonitorHealthWindow is not null)
        {
            await buildMonitorHealthWindow.WaitForInitialLoadAsync();
        }
    }

    private async Task ApplySettingsAndStartAsync()
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

            previousProjectHealth.Clear();
            previousProjectState.Clear();
            autoOpenedLogForFailure.Clear();
            fileChangeBuildStarts.Clear();
            ToastNotificationService.ApplySettings(currentSettings.AppBehavior);
            await orchestrator.StopAllAsync();
            orchestrator.ApplySettings(currentSettings);

            if (ShouldAutoStartAnyProjectsOnLaunch())
            {
                await orchestrator.StartActiveProjectsAsync(CancellationToken.None).ConfigureAwait(false);
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

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ApplyPendingHealthUi);
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
        }

        lock (pendingHealthUiSync)
        {
            if (pendingHealthSnapshots is not null
                && Interlocked.CompareExchange(ref healthUiUpdateScheduled, 1, 0) == 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ApplyPendingHealthUi);
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
                hoverPanel.Update(snapshots);
            }

            if (Volatile.Read(ref trayMenuOpen) == 0)
            {
                AutoOpenLogsOnFailureTransition(snapshots);
                ShowBuildToasts(snapshots);
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

    private void AutoOpenLogsOnFailureTransition(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        if (!currentSettings.Monitor.AutoOpenLogOnFailure)
        {
            return;
        }

        foreach (var snapshot in snapshots.Where(s => s.IsActive))
        {
            previousProjectHealth.TryGetValue(snapshot.ProjectId, out var previousHealth);

            if (snapshot.Health == MonitorHealth.Red && previousHealth != MonitorHealth.Red)
            {
                if (autoOpenedLogForFailure.Add(snapshot.ProjectId))
                {
                    var logKind = LogKindForFailure(snapshot.State);
                    OpenLogViewer(
                        snapshot.ProjectId,
                        snapshot.DisplayName,
                        logKind,
                        selectErrorsFilter: snapshot.ErrorCount > 0);
                }
            }
            else if (snapshot.Health != MonitorHealth.Red)
            {
                autoOpenedLogForFailure.Remove(snapshot.ProjectId);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        buildIconAnimationTimer?.Stop();
        buildIconAnimationTimer = null;
        dispatcherHealthProbe?.Dispose();
        dispatcherHealthProbe = null;
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
            Text = "Local Build Monitor",
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

        return icon;
    }

    private void ToggleStatusPanel()
    {
        CancelStatusPanelTimers();
        TrayScreenPlacement.CaptureFromCursor();
        EnsureHoverPanel();

        if (hoverPanel is { IsVisible: true })
        {
            hoverPanel.Hide();
            return;
        }

        hoverPanel!.Update(orchestrator?.GetHealthSnapshots() ?? []);
        hoverPanel.ShowNearTray();
    }

    private void ShowStatusPanel()
    {
        CancelStatusPanelTimers();
        TrayScreenPlacement.CaptureFromCursor();
        EnsureHoverPanel();
        hoverPanel!.Update(orchestrator?.GetHealthSnapshots() ?? []);
        hoverPanel.ShowNearTray();
    }

    private void CancelStatusPanelTimers() => hideStatusPanelTimer?.Stop();

    private void ScheduleHideStatusPanel()
    {
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
        if (!pointerOverStatusPanel)
        {
            hoverPanel?.Hide();
            FlushStatusPanelLayout();
        }
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

        hoverPanel = new HoverStatusPanel();
        hoverPanel.ApplyLayout(windowsLayoutStore.Layout.StatusPanel);
        hoverPanel.SizeChanged += (_, _) => ScheduleSaveStatusPanelLayout();
        ApplyThemeToUi();
        hoverPanel.ViewLogRequested += projectId => OpenLogViewerForProject(projectId);
        hoverPanel.CopyErrorsRequested += projectId =>
            RunTrayMenuBackgroundAction(() => CopyProjectErrorsAsync(projectId));
        hoverPanel.RestartAppRequested += projectId =>
            RunTrayMenuBackgroundAction(() => orchestrator!.RestartAppAsync(projectId, CancellationToken.None));
        hoverPanel.RebuildAndRestartRequested += projectId =>
            RunTrayMenuBackgroundAction(() => orchestrator!.RebuildAndRestartAsync(projectId, CancellationToken.None));
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
        hoverPanel.MouseEnter += (_, _) =>
        {
            pointerOverStatusPanel = true;
            hideStatusPanelTimer?.Stop();
        };
        hoverPanel.MouseLeave += (_, _) => ScheduleHideStatusPanel();
        hoverPanel.Deactivated += (_, _) => ScheduleHideStatusPanel();
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
        bool selectErrorsFilter = false)
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

        if (openLogViewers.TryGetValue(projectId, out var existing))
        {
            try
            {
                if (existing.IsLoaded)
                {
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

                    existing.Activate();
                    existing.Focus();
                    return;
                }
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
        viewer.Closed += (_, _) => openLogViewers.Remove(projectId);
        openLogViewers[projectId] = viewer;
        viewer.Show();

        if (logKind is not null)
        {
            viewer.SelectLogKind(logKind.Value);
        }

        if (selectErrorsFilter)
        {
            viewer.SelectErrorsFilter();
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

        diagnosticsWindow = new BuildDiagnosticsWindow(orchestrator.TriggerJournal, windowsLayoutStore!);
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

        buildMonitorHealthWindow = new BuildMonitorHealthWindow(windowsLayoutStore);
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

        currentSettings = window.Settings;
        ThemeService.ApplyTheme(currentSettings.AppBehavior.Theme);
        ToastNotificationService.ApplySettings(currentSettings.AppBehavior);
        WindowsStartupService.Apply(currentSettings.AppBehavior.RunOnLogon);
        ApplyThemeToUi();
        RebuildTrayMenu();

        var applyVersion = Interlocked.Increment(ref settingsApplyVersion);
        _ = ApplySettingsAndStartInBackgroundAsync(applyVersion);

        ToastNotificationService.ShowIfEnabled(
            "Settings saved",
            "Projects with start on launch enabled are starting in the background.",
            ToastKind.Success,
            UserNotificationCategory.Info);
    }

    private async Task ApplySettingsAndStartInBackgroundAsync(int applyVersion)
    {
        try
        {
            await Task.Run(ApplySettingsAndStartAsync).ConfigureAwait(false);
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
        if (Interlocked.Exchange(ref exitRequested, 1) != 0)
        {
            return;
        }

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

        // Defer until after the context menu finishes handling the click.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _ = ExitAsync());
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
        if (trayContextMenu is null)
        {
            return;
        }

        var active = currentSettings.Projects.Where(p => p.IsActiveInSession).ToList();

        trayContextMenu.Items.Clear();

        trayContextMenu.Items.Add(new Forms.ToolStripMenuItem(
            "Status",
            null,
            (_, _) => RunTrayMenuUiAction(ShowStatusPanel)));
        trayContextMenu.Items.Add(new Forms.ToolStripSeparator());

        if (currentSettings.AppBehavior.TrayMenuLayout == TrayMenuLayout.ByProject)
        {
            AddByProjectItems(trayContextMenu.Items, active);
        }
        else
        {
            trayContextMenu.Items.Add(BuildRebuildMenu(active));
            trayContextMenu.Items.Add(BuildRestartMenu(active));
            trayContextMenu.Items.Add(BuildRunTestsMenu(active));
            trayContextMenu.Items.Add(BuildStopMenu(active));
            trayContextMenu.Items.Add(BuildViewLogsMenu(active));
            trayContextMenu.Items.Add(BuildCleanOutputMenu(active));
        }

        trayContextMenu.Items.Add(new Forms.ToolStripSeparator());
        trayContextMenu.Items.Add(new Forms.ToolStripMenuItem(
            "Build diagnostics…",
            null,
            (_, _) => RunTrayMenuUiAction(ShowBuildDiagnostics)));
        trayContextMenu.Items.Add(new Forms.ToolStripMenuItem(
            "Build Monitor Health…",
            null,
            (_, _) => RunTrayMenuUiAction(ShowBuildMonitorHealth)));
        trayContextMenu.Items.Add(new Forms.ToolStripSeparator());
        trayContextMenu.Items.Add(new Forms.ToolStripMenuItem(
            "Settings",
            null,
            (_, _) => RunTrayMenuUiAction(() => _ = ShowSettingsAsync())));
        trayContextMenu.Items.Add(new Forms.ToolStripMenuItem(
            "Exit",
            null,
            (_, _) => RequestExit()));

        ApplyTrayMenuTheme();
    }

    private void ApplyTrayMenuTheme()
    {
        if (trayContextMenu is null)
        {
            return;
        }

        TrayMenuTheme.Apply(trayContextMenu, ThemeService.Resolve(currentSettings.AppBehavior.Theme));
    }

    private void AddByProjectItems(Forms.ToolStripItemCollection items, List<LocalProjectDefinition> active)
    {
        if (active.Count == 0)
        {
            items.Add(new Forms.ToolStripMenuItem("(No active projects)") { Enabled = false });
            return;
        }

        foreach (var project in active)
        {
            var id = project.Id;
            var restartable = project.RunOptions.RunMode != ProjectRunMode.None;
            var submenu = new Forms.ToolStripMenuItem(project.DisplayName);

            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Rebuild", null, (_, _) =>
                RunTrayMenuBackgroundAction(() => orchestrator!.RebuildAsync(id, CancellationToken.None))));

            if (restartable)
            {
                submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Restart app", null, (_, _) =>
                    RunTrayMenuBackgroundAction(() => orchestrator!.RestartAppAsync(id, CancellationToken.None))));
                submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Rebuild & restart", null, (_, _) =>
                    RunTrayMenuBackgroundAction(() => orchestrator!.RebuildAndRestartAsync(id, CancellationToken.None))));
            }

            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Run tests", null, (_, _) =>
                RunTrayMenuBackgroundAction(() => orchestrator!.RunTestsAsync(id, CancellationToken.None))));
            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Stop", null, (_, _) =>
                RunTrayMenuBackgroundAction(() => orchestrator!.StopProjectAsync(id))));
            submenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("View log", null, (_, _) =>
                RunTrayMenuUiAction(() => OpenLogViewerForProject(id))));
            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Clean build output", null, (_, _) =>
                RunTrayMenuBackgroundAction(() => orchestrator!.RepairBuildOutputAsync(id, CancellationToken.None))));

            items.Add(submenu);
        }
    }

    private Forms.ToolStripMenuItem BuildRebuildMenu(List<LocalProjectDefinition> active)
    {
        var menu = new Forms.ToolStripMenuItem("Rebuild");
        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            RunTrayMenuBackgroundAction(async () =>
            {
                foreach (var p in active)
                {
                    await orchestrator!.RebuildAsync(p.Id, CancellationToken.None);
                }
            })));

        if (active.Count > 0)
        {
            menu.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (var project in active)
            {
                var id = project.Id;
                menu.DropDownItems.Add(new Forms.ToolStripMenuItem(project.DisplayName, null, (_, _) =>
                    RunTrayMenuBackgroundAction(() => orchestrator!.RebuildAsync(id, CancellationToken.None))));
            }
        }

        return menu;
    }

    private Forms.ToolStripMenuItem BuildRestartMenu(List<LocalProjectDefinition> active)
    {
        var menu = new Forms.ToolStripMenuItem("Restart app");
        var restartable = active.Where(p => p.RunOptions.RunMode != ProjectRunMode.None).ToList();
        menu.Enabled = restartable.Count > 0;

        if (restartable.Count == 0)
        {
            return menu;
        }

        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("Restart all active", null, (_, _) =>
            RunTrayMenuBackgroundAction(async () =>
            {
                foreach (var p in restartable)
                {
                    await orchestrator!.RestartAppAsync(p.Id, CancellationToken.None);
                }
            })));

        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("Rebuild & restart all active", null, (_, _) =>
            RunTrayMenuBackgroundAction(async () =>
            {
                foreach (var p in restartable)
                {
                    await orchestrator!.RebuildAndRestartAsync(p.Id, CancellationToken.None);
                }
            })));

        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var project in restartable)
        {
            var id = project.Id;
            var name = project.DisplayName;
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem($"Restart — {name}", null, (_, _) =>
                RunTrayMenuBackgroundAction(() => orchestrator!.RestartAppAsync(id, CancellationToken.None))));
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem($"Rebuild & restart — {name}", null, (_, _) =>
                RunTrayMenuBackgroundAction(() => orchestrator!.RebuildAndRestartAsync(id, CancellationToken.None))));
        }

        return menu;
    }

    private Forms.ToolStripMenuItem BuildRunTestsMenu(List<LocalProjectDefinition> active)
    {
        var menu = new Forms.ToolStripMenuItem("Run tests") { Enabled = active.Count > 0 };
        if (active.Count == 0)
        {
            return menu;
        }

        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            RunTrayMenuUiAction(() => StartRunTestsForProjects(active))));

        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var project in active)
        {
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem(project.DisplayName, null, (_, _) =>
                RunTrayMenuUiAction(() => StartRunTestsForProjects([project]))));
        }

        return menu;
    }

    private void StartRunTestsForProjects(IReadOnlyList<LocalProjectDefinition> projects)
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

    private Forms.ToolStripMenuItem BuildStopMenu(List<LocalProjectDefinition> active)
    {
        var menu = new Forms.ToolStripMenuItem("Stop");
        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            RunTrayMenuBackgroundAction(() => orchestrator!.StopAllAsync())));

        if (active.Count > 0)
        {
            menu.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (var project in active)
            {
                var id = project.Id;
                menu.DropDownItems.Add(new Forms.ToolStripMenuItem(project.DisplayName, null, (_, _) =>
                    RunTrayMenuBackgroundAction(() => orchestrator!.StopProjectAsync(id))));
            }
        }

        return menu;
    }

    private Forms.ToolStripMenuItem BuildViewLogsMenu(List<LocalProjectDefinition> active)
    {
        var menu = new Forms.ToolStripMenuItem("View Log") { Enabled = active.Count > 0 };
        foreach (var project in active)
        {
            var id = project.Id;
            var name = project.DisplayName;
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) =>
                RunTrayMenuUiAction(() => OpenLogViewerForProject(id, name))));
        }

        return menu;
    }

    private Forms.ToolStripMenuItem BuildCleanOutputMenu(List<LocalProjectDefinition> active)
    {
        var menu = new Forms.ToolStripMenuItem("Clean build output") { Enabled = active.Count > 0 };
        if (active.Count == 0)
        {
            return menu;
        }

        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("All active", null, (_, _) =>
            RunTrayMenuBackgroundAction(async () =>
            {
                foreach (var project in active)
                {
                    await orchestrator!.RepairBuildOutputAsync(project.Id, CancellationToken.None);
                }
            })));

        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var project in active)
        {
            var id = project.Id;
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem(project.DisplayName, null, (_, _) =>
                RunTrayMenuBackgroundAction(() => orchestrator!.RepairBuildOutputAsync(id, CancellationToken.None))));
        }

        return menu;
    }

    private async Task RebuildAllActiveAsync()
    {
        foreach (var project in currentSettings.Projects.Where(p => p.IsActiveInSession))
        {
            await orchestrator!.RebuildAsync(project.Id, CancellationToken.None);
        }
    }

    private void ShowBuildToasts(IReadOnlyList<ProjectHealthSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots.Where(s => s.IsActive))
        {
            previousProjectState.TryGetValue(snapshot.ProjectId, out var previousState);
            var currentState = snapshot.State;

            if (currentState == ProjectLifecycleState.Building && previousState != ProjectLifecycleState.Building)
            {
                if (!fileChangeBuildStarts.Remove(snapshot.ProjectId))
                {
                    ToastNotificationService.ShowIfEnabled(
                        $"Building — {snapshot.DisplayName}",
                        "Build started.",
                        ToastKind.Info,
                        UserNotificationCategory.BuildStart);
                }
            }

            if (previousState == ProjectLifecycleState.Building
                && IsSuccessfulBuildEndState(currentState))
            {
                var message = snapshot.LastDuration is { } duration
                    ? $"Completed in {FormatBuildDuration(duration)}."
                    : "Build completed successfully.";
                ToastNotificationService.ShowIfEnabled(
                    $"Build succeeded — {snapshot.DisplayName}",
                    message,
                    ToastKind.Success,
                    UserNotificationCategory.BuildSuccess);
            }
            else if (previousState == ProjectLifecycleState.Testing && currentState == ProjectLifecycleState.TestOk)
            {
                ToastNotificationService.ShowIfEnabled(
                    $"Tests passed — {snapshot.DisplayName}",
                    "Tests completed successfully.",
                    ToastKind.Success,
                    UserNotificationCategory.BuildSuccess);
            }

            if ((previousState == ProjectLifecycleState.Building
                    || previousState == ProjectLifecycleState.Watching)
                && currentState == ProjectLifecycleState.BuildFailed)
            {
                var message = string.IsNullOrWhiteSpace(snapshot.LastErrorPreview)
                    ? "See build log for details."
                    : snapshot.LastErrorPreview;
                ToastNotificationService.ShowIfEnabled(
                    $"Build failed — {snapshot.DisplayName}",
                    message,
                    ToastKind.Error,
                    UserNotificationCategory.BuildFailure);
            }
            else if (previousState == ProjectLifecycleState.Testing && currentState == ProjectLifecycleState.TestFailed)
            {
                var message = string.IsNullOrWhiteSpace(snapshot.LastErrorPreview)
                    ? "See test log for details."
                    : snapshot.LastErrorPreview;
                ToastNotificationService.ShowIfEnabled(
                    $"Tests failed — {snapshot.DisplayName}",
                    message,
                    ToastKind.Error,
                    UserNotificationCategory.BuildFailure);
            }

            previousProjectState[snapshot.ProjectId] = currentState;
        }

        var activeIds = snapshots.Where(s => s.IsActive).Select(s => s.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in previousProjectState.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            previousProjectState.Remove(staleId);
        }
    }

    private static bool IsSuccessfulBuildEndState(ProjectLifecycleState state) =>
        state is ProjectLifecycleState.BuildOk
            or ProjectLifecycleState.Watching
            or ProjectLifecycleState.Running;

    private static string FormatBuildDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"h\:mm\:ss");
        }

        return duration.TotalMinutes >= 1
            ? duration.ToString(@"m\:ss")
            : $"{duration.TotalSeconds:F1}s";
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
        }

        var activeIds = snapshots.Where(s => s.IsActive).Select(s => s.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in previousProjectHealth.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            previousProjectHealth.Remove(staleId);
        }
    }

    private static AppSettings CloneSettings(AppSettings source) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(source)) ?? new AppSettings();

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
        notifyIcon.Text = FormatTrayTooltip(currentTrayHeadline, currentTrayHealth, currentTrayBuilding);
    }

    private static string FormatTrayTooltip(
        ProjectHealthSnapshot? headline,
        MonitorHealth health,
        bool isBuilding)
    {
        if (isBuilding)
        {
            var name = headline?.DisplayName ?? "project";
            return TruncateTrayText($"Building — {name}");
        }

        if (headline is null)
        {
            return DescribeHealthTooltip(health);
        }

        if (headline.Health == MonitorHealth.Red)
        {
            var phase = string.IsNullOrWhiteSpace(headline.FailurePhase)
                ? "Failed"
                : headline.FailurePhase;
            if (!string.IsNullOrWhiteSpace(headline.LastErrorPreview))
            {
                return TruncateTrayText($"{headline.DisplayName} — {phase}: {headline.LastErrorPreview}");
            }

            return TruncateTrayText($"{headline.DisplayName} — {phase}");
        }

        if (headline.Health == MonitorHealth.Amber)
        {
            return TruncateTrayText($"{headline.DisplayName} — Warnings");
        }

        if (headline.ListenUrlReady && !string.IsNullOrWhiteSpace(headline.ListenUrl))
        {
            return TruncateTrayText($"{headline.DisplayName} — Site up · {headline.ListenUrl}");
        }

        return TruncateTrayText($"{headline.DisplayName} — OK");
    }

    private static string TruncateTrayText(string text, int maxLength = 63) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    private static void MigrateLegacyAppDataIfNeeded(string newAppDataDirectory)
    {
        if (Directory.Exists(newAppDataDirectory))
        {
            return;
        }

        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AzureBuildMonitor");
        if (!Directory.Exists(legacyDirectory))
        {
            return;
        }

        try
        {
            Directory.Move(legacyDirectory, newAppDataDirectory);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Legacy app data migration failed: {ex.Message}");
        }
    }

    private static bool ShouldSkipProjectStart()
    {
        var value = Environment.GetEnvironmentVariable("BUILDMONITOR_SKIP_PROJECT_START");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldAutoStartAnyProjectsOnLaunch() =>
        !ShouldSkipProjectStart()
        && currentSettings.Projects.Any(p => p.IsActiveInSession && p.StartOnLaunch);

    private bool ShouldAutoOpenBuildMonitorHealth()
    {
        var value = Environment.GetEnvironmentVariable("BUILDMONITOR_AUTO_BUILD_MONITOR_HEALTH");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Environment.GetEnvironmentVariable("BUILDMONITOR_AUTO_THREAD_HEALTH");
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return currentSettings.Monitor.AutoOpenBuildMonitorHealthOnStartup;
    }

    private static string DescribeHealth(MonitorHealth health) =>
        health switch
        {
            MonitorHealth.Green => "OK",
            MonitorHealth.Amber => "Warnings",
            MonitorHealth.Red => "Errors",
            _ => "Unknown"
        };

    private static string DescribeHealthTooltip(MonitorHealth health) =>
        health switch
        {
            MonitorHealth.Green => "Build monitor - Success",
            MonitorHealth.Amber => "Build monitor - Warnings",
            MonitorHealth.Red => "Build monitor - Failed",
            _ => "Build Monitor"
        };
}
