using System.IO;
using System.Windows;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Services;
using BuildMonitor.TrayApp.Services;
using Forms = System.Windows.Forms;

namespace BuildMonitor.TrayApp;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? notifyIcon;
    private Forms.ContextMenuStrip? trayContextMenu;
    private Forms.ToolStripMenuItem? rebuildSubmenu;
    private Forms.ToolStripMenuItem? restartSubmenu;
    private Forms.ToolStripMenuItem? runTestsSubmenu;
    private Forms.ToolStripMenuItem? stopSubmenu;
    private Forms.ToolStripMenuItem? viewLogsSubmenu;
    private ProjectOrchestrator? orchestrator;
    private HoverStatusPanel? hoverPanel;
    private SettingsStore? settingsStore;
    private BuildLogViewerWindowStateStore? buildLogWindowStateStore;
    private AppSettings currentSettings = new();
    private string appDataDirectory = string.Empty;
    private DispatcherTimer? hideStatusPanelTimer;
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
    private int exitRequested;

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

        settingsStore = new SettingsStore(settingsPath);
        buildLogWindowStateStore = new BuildLogViewerWindowStateStore(
            Path.Combine(appDataDirectory, "build-log-window.json"));
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

        orchestrator = new ProjectOrchestrator(logsPath);
        orchestrator.HealthUpdated += OnHealthUpdated;
        orchestrator.UserNotification += OnUserNotification;

        ThemeService.ThemeChanged += OnThemeChanged;
        EnsureHoverPanel();
        ApplyThemeToUi();
        notifyIcon = BuildNotifyIcon();
        notifyIcon.Visible = true;

        await ApplySettingsAndStartAsync();
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
            await orchestrator.StartActiveProjectsAsync(CancellationToken.None);
        }
        finally
        {
            settingsApplyGate.Release();
        }
    }

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
            var toastKind = kind switch
            {
                UserNotificationKind.Error => ToastKind.Error,
                UserNotificationKind.Warning => ToastKind.Warning,
                _ => ToastKind.Info
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

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            var activeOnly = snapshots.Where(s => s.IsActive).ToList();
            UpdateTrayIcon(
                LocalTrayIconRollupEvaluator.Rollup(activeOnly),
                LocalTrayIconRollupEvaluator.IsBuilding(activeOnly));
            hoverPanel?.Update(snapshots);
            AutoOpenLogsOnFailureTransition(snapshots);
            ShowBuildToasts(snapshots);
            PlayBuildNotificationSounds(snapshots);
        });
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
                    OpenLogViewer(snapshot.ProjectId, snapshot.DisplayName);
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
            Icon = AppIconService.TrayIcon
        };

        trayContextMenu = new Forms.ContextMenuStrip();

        var statusItem = new Forms.ToolStripMenuItem("Status");
        statusItem.Click += (_, _) => RunTrayMenuUiAction(ShowStatusPanel);
        trayContextMenu.Items.Add(statusItem);

        trayContextMenu.Items.Add(new Forms.ToolStripSeparator());

        rebuildSubmenu = new Forms.ToolStripMenuItem("Rebuild");
        trayContextMenu.Items.Add(rebuildSubmenu);

        restartSubmenu = new Forms.ToolStripMenuItem("Restart app");
        trayContextMenu.Items.Add(restartSubmenu);

        runTestsSubmenu = new Forms.ToolStripMenuItem("Run tests");
        trayContextMenu.Items.Add(runTestsSubmenu);

        stopSubmenu = new Forms.ToolStripMenuItem("Stop");
        trayContextMenu.Items.Add(stopSubmenu);

        viewLogsSubmenu = new Forms.ToolStripMenuItem("View Log");
        trayContextMenu.Items.Add(viewLogsSubmenu);

        trayContextMenu.Items.Add(new Forms.ToolStripSeparator());
        trayContextMenu.Items.Add(new Forms.ToolStripMenuItem("Settings", null, (_, _) => RunTrayMenuUiAction(() => _ = ShowSettingsAsync())));
        trayContextMenu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => RequestExit()));

        trayContextMenu.Opening += (_, _) =>
        {
            try
            {
                RefreshProjectSubmenus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tray menu refresh failed: {ex}");
            }
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
                ToggleStatusPanel();
            }
        };

        return icon;
    }

    private void ToggleStatusPanel()
    {
        CancelStatusPanelTimers();
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
        }
    }

    private void EnsureHoverPanel()
    {
        if (hoverPanel is not null)
        {
            return;
        }

        hoverPanel = new HoverStatusPanel();
        ApplyThemeToUi();
        hoverPanel.ViewLogRequested += projectId => OpenLogViewer(projectId);
        hoverPanel.RestartAppRequested += projectId =>
            RunTrayMenuBackgroundAction(() => orchestrator!.RestartAppAsync(projectId, CancellationToken.None));
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

    private void OpenLogViewer(string projectId, string? displayName = null, BuildLogKind? logKind = null)
    {
        if (orchestrator is null)
        {
            return;
        }

        var name = displayName ?? currentSettings.Projects.FirstOrDefault(p => p.Id == projectId)?.DisplayName ?? projectId;
        if (buildLogWindowStateStore is null)
        {
            return;
        }

        if (openLogViewers.TryGetValue(projectId, out var existing))
        {
            try
            {
                if (existing.IsLoaded)
                {
                    if (!existing.IsVisible)
                    {
                        existing.Show();
                    }

                    if (logKind is not null)
                    {
                        existing.SelectLogKind(logKind.Value);
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
            buildLogWindowStateStore,
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
    }

    private async Task ShowSettingsAsync()
    {
        if (settingsStore is null)
        {
            return;
        }

        var window = new SettingsWindow(CloneSettings(currentSettings));
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

        var applyVersion = Interlocked.Increment(ref settingsApplyVersion);
        _ = ApplySettingsAndStartInBackgroundAsync(applyVersion);

        ToastNotificationService.ShowIfEnabled(
            "Settings saved",
            "Active projects are starting in the background.",
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

        trayContextMenu?.Hide();

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

    private void RefreshProjectSubmenus()
    {
        var active = currentSettings.Projects.Where(p => p.IsActiveInSession).ToList();

        BuildRebuildSubmenu(active);
        BuildRestartSubmenu(active);
        BuildRunTestsSubmenu(active);
        BuildStopSubmenu(active);
        BuildViewLogsSubmenu(active);

        if (trayContextMenu is not null)
        {
            TrayMenuTheme.Apply(trayContextMenu, ThemeService.CurrentResolved);
        }
    }

    private void BuildRebuildSubmenu(List<LocalProjectDefinition> active)
    {
        if (rebuildSubmenu is null) return;
        rebuildSubmenu.DropDownItems.Clear();

        rebuildSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            RunTrayMenuBackgroundAction(async () =>
            {
                foreach (var p in active)
                {
                    await orchestrator!.RebuildAsync(p.Id, CancellationToken.None);
                }
            })));

        if (active.Count > 0)
        {
            rebuildSubmenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (var project in active)
            {
                var id = project.Id;
                var name = project.DisplayName;
                rebuildSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) =>
                    RunTrayMenuBackgroundAction(() => orchestrator!.RebuildAsync(id, CancellationToken.None))));
            }
        }
    }

    private void BuildRestartSubmenu(List<LocalProjectDefinition> active)
    {
        if (restartSubmenu is null)
        {
            return;
        }

        restartSubmenu.DropDownItems.Clear();
        var restartable = active.Where(p => p.RunOptions.RunMode != ProjectRunMode.None).ToList();
        restartSubmenu.Enabled = restartable.Count > 0;

        if (restartable.Count == 0)
        {
            return;
        }

        restartSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            RunTrayMenuBackgroundAction(async () =>
            {
                foreach (var p in restartable)
                {
                    await orchestrator!.RestartAppAsync(p.Id, CancellationToken.None);
                }
            })));

        restartSubmenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var project in restartable)
        {
            var id = project.Id;
            var name = project.DisplayName;
            restartSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) =>
                RunTrayMenuBackgroundAction(() => orchestrator!.RestartAppAsync(id, CancellationToken.None))));
        }
    }

    private void BuildRunTestsSubmenu(List<LocalProjectDefinition> active)
    {
        if (runTestsSubmenu is null)
        {
            return;
        }

        runTestsSubmenu.DropDownItems.Clear();
        runTestsSubmenu.Enabled = active.Count > 0;

        if (active.Count == 0)
        {
            return;
        }

        runTestsSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            RunTrayMenuUiAction(() => StartRunTestsForProjects(active))));

        runTestsSubmenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var project in active)
        {
            var id = project.Id;
            var name = project.DisplayName;
            runTestsSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) =>
                RunTrayMenuUiAction(() => StartRunTestsForProjects([project]))));
        }
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

    private void BuildStopSubmenu(List<LocalProjectDefinition> active)
    {
        if (stopSubmenu is null) return;
        stopSubmenu.DropDownItems.Clear();

        stopSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            RunTrayMenuBackgroundAction(() => orchestrator!.StopAllAsync())));

        if (active.Count > 0)
        {
            stopSubmenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (var project in active)
            {
                var id = project.Id;
                var name = project.DisplayName;
                stopSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) =>
                    RunTrayMenuBackgroundAction(() => orchestrator!.StopProjectAsync(id))));
            }
        }
    }

    private void BuildViewLogsSubmenu(List<LocalProjectDefinition> active)
    {
        if (viewLogsSubmenu is null) return;
        viewLogsSubmenu.DropDownItems.Clear();

        if (active.Count == 0)
        {
            viewLogsSubmenu.Enabled = false;
            return;
        }

        viewLogsSubmenu.Enabled = true;
        foreach (var project in active)
        {
            var id = project.Id;
            var name = project.DisplayName;
            viewLogsSubmenu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) =>
                RunTrayMenuUiAction(() => OpenLogViewer(id, name))));
        }
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

            if (previousState == ProjectLifecycleState.Building && currentState == ProjectLifecycleState.BuildOk)
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

            if (previousState == ProjectLifecycleState.Building && currentState == ProjectLifecycleState.BuildFailed)
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
        if (trayContextMenu is not null)
        {
            TrayMenuTheme.Apply(trayContextMenu, theme);
        }
    }

    private void OnThemeChanged(ResolvedTheme theme) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            hoverPanel?.ApplyTheme(theme);
            if (trayContextMenu is not null)
            {
                TrayMenuTheme.Apply(trayContextMenu, theme);
            }
        });

    private void UpdateTrayIcon(MonitorHealth health, bool isBuilding)
    {
        if (notifyIcon is null)
        {
            return;
        }

        currentTrayHealth = health;
        currentTrayBuilding = isBuilding;

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
            buildIconAnimationFrame);
        notifyIcon.Text = currentTrayBuilding
            ? $"Build monitor - Building ({DescribeHealth(currentTrayHealth)})"
            : DescribeHealthTooltip(currentTrayHealth);
    }

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
