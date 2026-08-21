using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Security;
using BuildMonitor.TrayApp.Services;
using Microsoft.Win32;

namespace BuildMonitor.TrayApp;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<MonitoredProjectSettings> projectItems = [];
    private MonitoredProjectSettings? selectedProject;

    private static LocalProjectAttachment EnsureLocal(MonitoredProjectSettings project) =>
        project.Local ??= new LocalProjectAttachment();
    private bool isLoadingEditor;
    private readonly AppThemePreference themeAtOpen;

    private readonly AppWindowsLayoutStore windowsLayoutStore;
    private readonly AzureDevOpsDiscoveryClient azureDiscoveryClient = new();
    private readonly AzureConnectionSettingsEditor azureConnectionEditor;

    public AppSettings Settings { get; }

    public SettingsWindow(AppSettings settings, AppWindowsLayoutStore windowsLayoutStore)
    {
        this.windowsLayoutStore = windowsLayoutStore;
        InitializeComponent();
        Settings = settings;
        themeAtOpen = Settings.AppBehavior.Theme;
        azureConnectionEditor = new AzureConnectionSettingsEditor(
            Settings,
            new AzureConnectionSecretStore(AzureConnectionSecretStore.DefaultSecretsDirectory, new DpapiSecretProtector()),
            azureDiscoveryClient);

        RunModeCombo.ItemsSource = Enum.GetValues<ProjectRunMode>();
        BuildControlModeCombo.Items.Clear();
        BuildControlModeCombo.Items.Add(new ComboBoxItem
        {
            Content = "File Watching",
            Tag = ProjectBuildControlMode.FileWatching
        });
        BuildControlModeCombo.Items.Add(new ComboBoxItem
        {
            Content = "AI Controlled",
            Tag = ProjectBuildControlMode.AiControlled
        });
        PreferredSiteUrlCombo.Items.Clear();
        PreferredSiteUrlCombo.Items.Add(new ComboBoxItem
        {
            Content = PreferredSiteUrlSchemeDisplay.ToLabel(PreferredSiteUrlScheme.Auto),
            Tag = PreferredSiteUrlScheme.Auto
        });
        PreferredSiteUrlCombo.Items.Add(new ComboBoxItem
        {
            Content = PreferredSiteUrlSchemeDisplay.ToLabel(PreferredSiteUrlScheme.Https),
            Tag = PreferredSiteUrlScheme.Https
        });
        PreferredSiteUrlCombo.Items.Add(new ComboBoxItem
        {
            Content = PreferredSiteUrlSchemeDisplay.ToLabel(PreferredSiteUrlScheme.Http),
            Tag = PreferredSiteUrlScheme.Http
        });
        RunTestsCombo.ItemsSource = Enum.GetValues<TestRunTrigger>();
        AutoOpenLogCombo.ItemsSource = Enum.GetValues<AutoOpenLogMode>();
        FileChangesCombo.ItemsSource = Enum.GetValues<FileChangeMode>();
        ThemeCombo.ItemsSource = Enum.GetValues<AppThemePreference>();
        ThemeCombo.SelectedItem = Settings.AppBehavior.Theme;
        ToastPositionCombo.ItemsSource = Enum.GetValues<ToastPosition>();
        ToastPositionCombo.SelectedItem = Settings.AppBehavior.ToastPosition;
        TrayMenuLayoutCombo.ItemsSource = Enum.GetValues<TrayMenuLayout>();
        TrayMenuLayoutCombo.SelectedItem = Settings.AppBehavior.TrayMenuLayout;
        ToastDurationText.Text = Settings.AppBehavior.ToastDurationSeconds.ToString();
        ToastOnBuildStartCheck.IsChecked = Settings.AppBehavior.Toasts.BuildStart;
        ToastOnFileChangeCheck.IsChecked = Settings.AppBehavior.Toasts.FileChangeDetected;
        ToastOnBuildSuccessCheck.IsChecked = Settings.AppBehavior.Toasts.BuildSuccess;
        ToastOnBuildFailureCheck.IsChecked = Settings.AppBehavior.Toasts.BuildFailure;
        ToastOnWarningsCheck.IsChecked = Settings.AppBehavior.Toasts.Warnings;
        ToastOnErrorsCheck.IsChecked = Settings.AppBehavior.Toasts.Errors;
        ToastOnInfoCheck.IsChecked = Settings.AppBehavior.Toasts.Info;

        MaxConcurrentText.Text = Settings.Monitor.MaxConcurrentActiveProjects.ToString();
        DebounceMsText.Text = Settings.Monitor.FileChangeDebounceMs.ToString();
        DebounceModeCombo.ItemsSource = Enum.GetValues<FileChangeDebounceMode>();
        DebounceModeCombo.SelectedItem = Settings.Monitor.FileChangeDebounceMode;
        UpdateDebounceModeUi();
        CoalesceWatchRebuildsCheck.IsChecked = Settings.Monitor.CoalesceWatchRebuilds;
        DeferStartupBuildUntilQuietCheck.IsChecked = Settings.Monitor.DeferStartupBuildUntilQuiet;
        CancelSupersededBuildsCheck.IsChecked = Settings.Monitor.CancelSupersededBuilds;
        UseAgentTranscriptActivityCheck.IsChecked = Settings.Monitor.UseAgentTranscriptActivity;
        LearnFromDiagnosticsVerdictsCheck.IsChecked = Settings.Monitor.LearnFromDiagnosticsVerdicts;
        HealthRefreshText.Text = Settings.Monitor.HealthRefreshSeconds.ToString();
        AutoOpenBuildMonitorHealthCheck.IsChecked = Settings.Monitor.AutoOpenBuildMonitorHealthOnStartup;
        PlaySoundOnErrorCheck.IsChecked = Settings.Monitor.PlaySoundOnBuildError;
        PlaySoundOnSuccessCheck.IsChecked = Settings.Monitor.PlaySoundOnBuildSuccess;
        MaxLogBytesText.Text = Settings.Monitor.MaxLogDisplayBytes.ToString();
        ControlPlaneEnabledCheck.IsChecked = Settings.Monitor.ControlPlaneEnabled;
        ControlPlanePortText.Text = Settings.Monitor.ControlPlanePort.ToString();
        ControlPlaneBusyTimeoutText.Text = Settings.Monitor.ControlPlaneBusyTimeoutSeconds.ToString();
        SuppressAutoBuildTestsCheck.IsChecked = Settings.Monitor.SuppressAutoBuildTests;
        StartMinimizedCheck.IsChecked = Settings.AppBehavior.StartMinimizedToTray;
        RunOnLogonCheck.IsChecked = Settings.AppBehavior.RunOnLogon;
        FollowStatusPanelDesktopCheck.IsChecked = Settings.AppBehavior.FollowStatusPanelToVirtualDesktop;
        FollowBuildLogDesktopCheck.IsChecked = Settings.AppBehavior.FollowBuildLogToVirtualDesktop;

        foreach (var project in Settings.Projects)
        {
            projectItems.Add(project);
        }

        ProjectsList.ItemsSource = projectItems;
        if (projectItems.Count > 0)
        {
            ProjectsList.SelectedIndex = 0;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        WindowLayoutService.Capture(this, windowsLayoutStore.Layout.Settings);
        _ = windowsLayoutStore.SaveAsync();
        azureDiscoveryClient.Dispose();

        if (DialogResult != true)
        {
            ThemeService.ApplyTheme(themeAtOpen);
        }

        base.OnClosing(e);
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        WindowLayoutService.Apply(this, windowsLayoutStore.Layout.Settings, 980, 820);
        if (double.IsNaN(windowsLayoutStore.Layout.Settings.Left))
        {
            TrayScreenPlacement.PlaceWindowCentered(this);
        }

        UpdateProjectStartBlockedHint();

        var theme = ThemeService.Resolve(Settings.AppBehavior.Theme);
        ThemeService.ApplyToWindow(this, theme);

        await azureConnectionEditor.LoadAsync(CancellationToken.None);
        AzureDisplayNameText.Text = azureConnectionEditor.DraftDisplayName;
        AzureOrganizationUrlText.Text = azureConnectionEditor.DraftOrganizationUrl;
        AzureCredentialStatusText.Text = azureConnectionEditor.CredentialStatusText;
        AzurePatBox.Password = string.Empty;
        AzureConnectionResultText.Text = string.Empty;
    }

    private async void AzureTestConnectionClicked(object sender, RoutedEventArgs e)
    {
        SyncAzureDraftFromUi();
        AzureConnectionResultText.Text = "Testing connection…";
        try
        {
            var result = await azureConnectionEditor.TestConnectionAsync(CancellationToken.None);
            AzureConnectionResultText.Text = $"{result.Outcome}: {result.Message}";
        }
        catch (Exception ex)
        {
            AzureConnectionResultText.Text = $"Unexpected error: {ex.Message}";
        }
    }

    private void SyncAzureDraftFromUi()
    {
        azureConnectionEditor.DraftDisplayName = AzureDisplayNameText.Text?.Trim() ?? string.Empty;
        azureConnectionEditor.DraftOrganizationUrl = AzureOrganizationUrlText.Text?.Trim() ?? string.Empty;
        azureConnectionEditor.SetPendingPat(AzurePatBox.Password);
    }

    private void UpdateProjectStartBlockedHint()
    {
        var value = Environment.GetEnvironmentVariable("BUILDMONITOR_SKIP_PROJECT_START");
        var blocked = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        ProjectStartBlockedHint.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ThemeComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemeCombo.SelectedItem is not AppThemePreference preference)
        {
            return;
        }

        ThemeService.ApplyTheme(preference);
        ThemeService.ApplyToWindow(this, ThemeService.Resolve(preference));
    }

    private void ProjectsListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitEditorToSelected();
        selectedProject = ProjectsList.SelectedItem as MonitoredProjectSettings;
        EditorPanel.IsEnabled = selectedProject is not null;
        if (selectedProject is null)
        {
            AgentSkillStatusSummary.Text = "No project selected";
            AgentSkillStatusDetail.Text = "Select a project to see Cursor agent integration status.";
            InstallAgentSkillButton.Content = "Install / Update";
            InstallAgentSkillButton.IsEnabled = false;
            return;
        }

        LoadEditorFromProject(selectedProject);
    }

    private void LoadEditorFromProject(MonitoredProjectSettings project)
    {
        isLoadingEditor = true;
        try
        {
            DisplayNameText.Text = project.DisplayName;
            RootFolderText.Text = EnsureLocal(project).RootFolder;
            ProjectFileText.Text = EnsureLocal(project).ProjectFile;
            ExtraArgsText.Text = EnsureLocal(project).ExtraDotNetArgs;
            RunModeCombo.SelectedItem = EnsureLocal(project).RunOptions.RunMode;
            SelectBuildControlMode(EnsureLocal(project).BuildControlMode);
            SelectPreferredSiteUrlScheme(EnsureLocal(project).PreferredSiteUrlScheme);
            StartOnLaunchCheck.IsChecked = EnsureLocal(project).StartOnLaunch;
            RestartOnCrashCheck.IsChecked = EnsureLocal(project).RunOptions.RestartOnCrash;
            MaxRetriesText.Text = EnsureLocal(project).RunOptions.MaxRestartRetries.ToString();
            AutoRestartOnWatchChangesCheck.IsChecked = EnsureLocal(project).RunOptions.AutoRestartOnWatchChanges;
            AutoRestartOnHotReloadRequestCheck.IsChecked = EnsureLocal(project).RunOptions.AutoRestartOnHotReloadRequest;
            RestartAppAfterRebuildCheck.IsChecked = EnsureLocal(project).RunOptions.RestartAppAfterRebuild;
            RunTestsCombo.SelectedItem = EnsureLocal(project).RunOptions.RunTests;
            AutoOpenLogCombo.SelectedItem = EnsureLocal(project).RunOptions.AutoOpenLog;
            ShowStatusPanelWhileBuildingCheck.IsChecked = EnsureLocal(project).RunOptions.ShowStatusPanelWhileBuilding;
            FileChangesCombo.SelectedItem = EnsureLocal(project).RunOptions.FileChanges;
            WatchExcludeSegmentsText.Text = EnsureLocal(project).RunOptions.WatchExcludeSegments;
            ReleaseOutputLocksCheck.IsChecked = EnsureLocal(project).RunOptions.ReleaseOutputLocksBeforeBuild;
            ForceCompleteWarningCountsCheck.IsChecked = EnsureLocal(project).RunOptions.ForceCompleteWarningCounts;
            AutoRepairCorruptedOutputCheck.IsChecked = EnsureLocal(project).RunOptions.AutoRepairCorruptedOutput;
            ReloadLaunchProfiles(selectCurrent: true);
            ReloadTestProjectCandidates(selectCurrent: true);
            RefreshAgentSkillStatus();
        }
        finally
        {
            isLoadingEditor = false;
        }
    }

    private void SelectBuildControlMode(ProjectBuildControlMode mode)
    {
        foreach (ComboBoxItem item in BuildControlModeCombo.Items)
        {
            if (item.Tag is ProjectBuildControlMode tag && tag == mode)
            {
                BuildControlModeCombo.SelectedItem = item;
                return;
            }
        }

        BuildControlModeCombo.SelectedIndex = 0;
    }

    private ProjectBuildControlMode ResolveBuildControlMode()
    {
        if (BuildControlModeCombo.SelectedItem is ComboBoxItem { Tag: ProjectBuildControlMode mode })
        {
            return mode;
        }

        return ProjectBuildControlMode.FileWatching;
    }

    private void SelectPreferredSiteUrlScheme(PreferredSiteUrlScheme scheme)
    {
        foreach (ComboBoxItem item in PreferredSiteUrlCombo.Items)
        {
            if (item.Tag is PreferredSiteUrlScheme tag && tag == scheme)
            {
                PreferredSiteUrlCombo.SelectedItem = item;
                return;
            }
        }

        PreferredSiteUrlCombo.SelectedIndex = 0;
    }

    private PreferredSiteUrlScheme ResolvePreferredSiteUrlScheme()
    {
        if (PreferredSiteUrlCombo.SelectedItem is ComboBoxItem { Tag: PreferredSiteUrlScheme scheme })
        {
            return scheme;
        }

        return PreferredSiteUrlScheme.Auto;
    }

    private void DisplayNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (isLoadingEditor || selectedProject is null)
        {
            return;
        }

        selectedProject.DisplayName = DisplayNameText.Text;
    }

    private void ProjectFileTextLostFocus(object sender, RoutedEventArgs e)
    {
        ReloadLaunchProfiles(selectCurrent: true);
        ReloadTestProjectCandidates(selectCurrent: true);
    }

    private void TestProjectComboLostFocus(object sender, RoutedEventArgs e)
    {
        if (isLoadingEditor || selectedProject is null)
        {
            return;
        }

        EnsureLocal(selectedProject).TestProjectFile = TestProjectCombo.Text.Trim();
    }

    private void LaunchProfileComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingEditor || selectedProject is null || LaunchProfileCombo.SelectedItem is null)
        {
            return;
        }

        EnsureLocal(selectedProject).LaunchProfile = LaunchProfileCombo.SelectedItem.ToString() ?? string.Empty;
    }

    private void ReloadLaunchProfiles(bool selectCurrent)
    {
        var profiles = LaunchProfileDiscovery.DiscoverProfiles(
            RootFolderText.Text.Trim(),
            ProjectFileText.Text.Trim());

        var current = selectCurrent
            ? (LaunchProfileCombo.Text.Trim().Length > 0 ? LaunchProfileCombo.Text.Trim() : selectedProject?.Local?.LaunchProfile)
            : selectedProject?.Local?.LaunchProfile;

        LaunchProfileCombo.ItemsSource = profiles;

        if (!string.IsNullOrWhiteSpace(current))
        {
            if (profiles.Contains(current))
            {
                LaunchProfileCombo.SelectedItem = current;
            }
            else
            {
                LaunchProfileCombo.Text = current;
            }
        }
        else if (profiles.Count > 0)
        {
            var preferred = LaunchProfileDiscovery.GetPreferredProfile(profiles);
            LaunchProfileCombo.SelectedItem = preferred;
            if (selectedProject is not null && preferred is not null)
            {
                EnsureLocal(selectedProject).LaunchProfile = preferred;
            }
        }
        else
        {
            LaunchProfileCombo.Text = string.Empty;
        }
    }

    private void ReloadTestProjectCandidates(bool selectCurrent)
    {
        var root = RootFolderText.Text.Trim();
        var projectFile = ProjectFileText.Text.Trim();
        var absoluteCandidates = TestProjectDiscovery.DiscoverCandidates(root, projectFile);
        var candidates = absoluteCandidates
            .Select(path => LaunchProfileDiscovery.ToRelativePath(root, path))
            .ToList();

        var current = selectCurrent
            ? (TestProjectCombo.Text.Trim().Length > 0 ? TestProjectCombo.Text.Trim() : selectedProject?.Local?.TestProjectFile)
            : selectedProject?.Local?.TestProjectFile;

        TestProjectCombo.ItemsSource = candidates;

        if (!string.IsNullOrWhiteSpace(current))
        {
            if (candidates.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                TestProjectCombo.SelectedItem = candidates.First(c =>
                    string.Equals(c, current, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                TestProjectCombo.Text = current;
            }
        }
        else
        {
            var resolution = TestProjectDiscovery.Resolve(root, projectFile, null);
            if (resolution.AutoDiscovered && resolution.Targets.Count == 1)
            {
                var relative = LaunchProfileDiscovery.ToRelativePath(root, resolution.Targets[0]);
                if (candidates.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    TestProjectCombo.SelectedItem = relative;
                }
                else
                {
                    TestProjectCombo.Text = relative;
                }

                if (selectedProject is not null && selectCurrent)
                {
                    EnsureLocal(selectedProject).TestProjectFile = string.Empty;
                }
            }
            else
            {
                TestProjectCombo.Text = string.Empty;
            }
        }
    }

    private void CommitEditorToSelected()
    {
        if (selectedProject is null)
        {
            return;
        }

        selectedProject.DisplayName = DisplayNameText.Text.Trim();
        EnsureLocal(selectedProject).RootFolder = RootFolderText.Text.Trim();
        EnsureLocal(selectedProject).ProjectFile = ProjectFileText.Text.Trim();
        EnsureLocal(selectedProject).LaunchProfile = LaunchProfileCombo.Text.Trim();
        EnsureLocal(selectedProject).TestProjectFile = TestProjectCombo.Text.Trim();
        EnsureLocal(selectedProject).ExtraDotNetArgs = ExtraArgsText.Text.Trim();
        EnsureLocal(selectedProject).RunOptions.RunMode = (ProjectRunMode)(RunModeCombo.SelectedItem ?? ProjectRunMode.Watch);
        EnsureLocal(selectedProject).BuildControlMode = ResolveBuildControlMode();
        EnsureLocal(selectedProject).PreferredSiteUrlScheme = ResolvePreferredSiteUrlScheme();
        EnsureLocal(selectedProject).StartOnLaunch = StartOnLaunchCheck.IsChecked == true;
        EnsureLocal(selectedProject).RunOptions.RestartOnCrash = RestartOnCrashCheck.IsChecked == true;
        if (int.TryParse(MaxRetriesText.Text, out var retries))
        {
            EnsureLocal(selectedProject).RunOptions.MaxRestartRetries = retries;
        }

        EnsureLocal(selectedProject).RunOptions.AutoRestartOnWatchChanges = AutoRestartOnWatchChangesCheck.IsChecked == true;
        EnsureLocal(selectedProject).RunOptions.AutoRestartOnHotReloadRequest = AutoRestartOnHotReloadRequestCheck.IsChecked == true;
        EnsureLocal(selectedProject).RunOptions.RestartAppAfterRebuild = RestartAppAfterRebuildCheck.IsChecked == true;

        EnsureLocal(selectedProject).RunOptions.RunTests = (TestRunTrigger)(RunTestsCombo.SelectedItem ?? TestRunTrigger.Off);
        EnsureLocal(selectedProject).RunOptions.AutoOpenLog = (AutoOpenLogMode)(AutoOpenLogCombo.SelectedItem ?? AutoOpenLogMode.Never);
        EnsureLocal(selectedProject).RunOptions.ShowStatusPanelWhileBuilding = ShowStatusPanelWhileBuildingCheck.IsChecked == true;
        EnsureLocal(selectedProject).RunOptions.FileChanges = (FileChangeMode)(FileChangesCombo.SelectedItem ?? FileChangeMode.WatchOnly);
        EnsureLocal(selectedProject).RunOptions.WatchExcludeSegments = WatchExcludeSegmentsText.Text.Trim();
        EnsureLocal(selectedProject).RunOptions.ReleaseOutputLocksBeforeBuild = ReleaseOutputLocksCheck.IsChecked == true;
        EnsureLocal(selectedProject).RunOptions.ForceCompleteWarningCounts = ForceCompleteWarningCountsCheck.IsChecked == true;
        EnsureLocal(selectedProject).RunOptions.AutoRepairCorruptedOutput = AutoRepairCorruptedOutputCheck.IsChecked == true;
    }

    private void AddProjectClicked(object sender, RoutedEventArgs e)
    {
        CommitEditorToSelected();
        var project = new MonitoredProjectSettings
        {
            DisplayName = "New project",
            Local = new LocalProjectAttachment { RootFolder = Environment.CurrentDirectory }
        };
        projectItems.Add(project);
        ProjectsList.SelectedItem = project;
    }

    private void RemoveProjectClicked(object sender, RoutedEventArgs e)
    {
        if (selectedProject is null)
        {
            return;
        }

        projectItems.Remove(selectedProject);
        selectedProject = null;
        EditorPanel.IsEnabled = false;
    }

    private void BrowseFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            RootFolderText.Text = dialog.FolderName;
            if (selectedProject is not null)
            {
                EnsureLocal(selectedProject).RootFolder = dialog.FolderName;
            }

            ReloadLaunchProfiles(selectCurrent: true);
            ReloadTestProjectCandidates(selectCurrent: true);
            RefreshAgentSkillStatus();
        }
    }

    private void RefreshAgentSkillStatusClicked(object sender, RoutedEventArgs e) =>
        RefreshAgentSkillStatus();

    private void RefreshAgentSkillStatus()
    {
        var root = RootFolderText.Text.Trim();
        if (string.IsNullOrWhiteSpace(root) && selectedProject is not null)
        {
            root = EnsureLocal(selectedProject).RootFolder;
        }

        var status = ControlPlaneAgentSkillInstaller.Inspect(root);
        AgentSkillStatusSummary.Text = status.Summary;
        AgentSkillStatusDetail.Text = status.Detail;
        InstallAgentSkillButton.Content = status.State switch
        {
            ControlPlaneAgentIntegrationState.Missing => "Install",
            ControlPlaneAgentIntegrationState.Current => "Reinstall",
            _ => "Install / Update"
        };
        InstallAgentSkillButton.IsEnabled = !string.IsNullOrWhiteSpace(root);
    }

    private void InstallAgentSkillClicked(object sender, RoutedEventArgs e)
    {
        var root = RootFolderText.Text.Trim();
        if (string.IsNullOrWhiteSpace(root) && selectedProject is not null)
        {
            root = EnsureLocal(selectedProject).RootFolder;
        }

        var result = ControlPlaneAgentSkillInstaller.Install(root);
        RefreshAgentSkillStatus();
        if (result.Ok)
        {
            System.Windows.MessageBox.Show(
                this,
                "Installed Cursor agent integration for this repo:\n\n"
                + $"Skill: {result.DestinationPath}\n"
                + $"Always-on rule: {result.RuleDestinationPath}\n\n"
                + "New agent chats in this workspace use BuildMonitor busy/idle/ship-check automatically — no paste required.",
                "Control plane skill",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        System.Windows.MessageBox.Show(
            this,
            result.Error ?? "Install failed.",
            "Control plane skill",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void BrowseProjectFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = ".NET projects|*.csproj;*.sln|All files|*.*",
            InitialDirectory = Directory.Exists(RootFolderText.Text)
                ? RootFolderText.Text
                : Environment.CurrentDirectory
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var root = RootFolderText.Text.Trim();
        ProjectFileText.Text = string.IsNullOrWhiteSpace(root)
            ? dialog.FileName
            : LaunchProfileDiscovery.ToRelativePath(root, dialog.FileName);

        if (selectedProject is not null)
        {
            EnsureLocal(selectedProject).ProjectFile = ProjectFileText.Text;
            if (string.IsNullOrWhiteSpace(selectedProject.DisplayName) ||
                selectedProject.DisplayName.Equals("New project", StringComparison.OrdinalIgnoreCase))
            {
                selectedProject.DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName);
                DisplayNameText.Text = selectedProject.DisplayName;
            }
        }

        ReloadLaunchProfiles(selectCurrent: false);
        ReloadTestProjectCandidates(selectCurrent: false);
    }

    private void CommitMonitorAndAppSettings()
    {
        if (int.TryParse(MaxConcurrentText.Text, out var maxConcurrent))
        {
            Settings.Monitor.MaxConcurrentActiveProjects = maxConcurrent;
        }

        if (int.TryParse(DebounceMsText.Text, out var debounce))
        {
            Settings.Monitor.FileChangeDebounceMs = debounce;
        }

        if (DebounceModeCombo.SelectedItem is FileChangeDebounceMode debounceMode)
        {
            Settings.Monitor.FileChangeDebounceMode = debounceMode;
        }

        if (int.TryParse(HealthRefreshText.Text, out var refresh))
        {
            Settings.Monitor.HealthRefreshSeconds = refresh;
        }

        Settings.Monitor.CoalesceWatchRebuilds = CoalesceWatchRebuildsCheck.IsChecked == true;
        Settings.Monitor.DeferStartupBuildUntilQuiet = DeferStartupBuildUntilQuietCheck.IsChecked == true;
        Settings.Monitor.CancelSupersededBuilds = CancelSupersededBuildsCheck.IsChecked == true;
        Settings.Monitor.UseAgentTranscriptActivity = UseAgentTranscriptActivityCheck.IsChecked == true;
        Settings.Monitor.LearnFromDiagnosticsVerdicts = LearnFromDiagnosticsVerdictsCheck.IsChecked == true;
        Settings.Monitor.ControlPlaneEnabled = ControlPlaneEnabledCheck.IsChecked == true;
        if (int.TryParse(ControlPlanePortText.Text, out var controlPlanePort))
        {
            Settings.Monitor.ControlPlanePort = controlPlanePort;
        }

        if (int.TryParse(ControlPlaneBusyTimeoutText.Text, out var busyTimeout))
        {
            Settings.Monitor.ControlPlaneBusyTimeoutSeconds = busyTimeout;
        }

        Settings.Monitor.SuppressAutoBuildTests = SuppressAutoBuildTestsCheck.IsChecked == true;
        Settings.Monitor.AutoOpenBuildMonitorHealthOnStartup = AutoOpenBuildMonitorHealthCheck.IsChecked == true;
        Settings.Monitor.PlaySoundOnBuildError = PlaySoundOnErrorCheck.IsChecked == true;
        Settings.Monitor.PlaySoundOnBuildSuccess = PlaySoundOnSuccessCheck.IsChecked == true;

        if (int.TryParse(MaxLogBytesText.Text, out var maxBytes))
        {
            Settings.Monitor.MaxLogDisplayBytes = maxBytes;
        }

        if (ThemeCombo.SelectedItem is AppThemePreference theme)
        {
            Settings.AppBehavior.Theme = theme;
        }

        Settings.AppBehavior.StartMinimizedToTray = StartMinimizedCheck.IsChecked == true;
        Settings.AppBehavior.RunOnLogon = RunOnLogonCheck.IsChecked == true;
        Settings.AppBehavior.FollowStatusPanelToVirtualDesktop = FollowStatusPanelDesktopCheck.IsChecked == true;
        Settings.AppBehavior.FollowBuildLogToVirtualDesktop = FollowBuildLogDesktopCheck.IsChecked == true;

        if (TrayMenuLayoutCombo.SelectedItem is TrayMenuLayout trayMenuLayout)
        {
            Settings.AppBehavior.TrayMenuLayout = trayMenuLayout;
        }

        if (ToastPositionCombo.SelectedItem is ToastPosition toastPosition)
        {
            Settings.AppBehavior.ToastPosition = toastPosition;
        }

        if (int.TryParse(ToastDurationText.Text, out var toastDuration))
        {
            Settings.AppBehavior.ToastDurationSeconds = toastDuration;
        }

        Settings.AppBehavior.Toasts.BuildStart = ToastOnBuildStartCheck.IsChecked == true;
        Settings.AppBehavior.Toasts.FileChangeDetected = ToastOnFileChangeCheck.IsChecked == true;
        Settings.AppBehavior.Toasts.BuildSuccess = ToastOnBuildSuccessCheck.IsChecked == true;
        Settings.AppBehavior.Toasts.BuildFailure = ToastOnBuildFailureCheck.IsChecked == true;
        Settings.AppBehavior.Toasts.Warnings = ToastOnWarningsCheck.IsChecked == true;
        Settings.AppBehavior.Toasts.Errors = ToastOnErrorsCheck.IsChecked == true;
        Settings.AppBehavior.Toasts.Info = ToastOnInfoCheck.IsChecked == true;

        WindowsStartupService.Apply(Settings.AppBehavior.RunOnLogon);
    }

    private void DebounceModeComboSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDebounceModeUi();

    private void UpdateDebounceModeUi()
    {
        if (!IsLoaded)
        {
            return;
        }

        var auto = DebounceModeCombo.SelectedItem is FileChangeDebounceMode.Auto;
        DebounceMsText.IsEnabled = !auto;
        LearnedDebounceHintText.Text = auto
            ? "Auto learns per project from save burst length (p90 × 1.25, smoothed, 1500–12000 ms). The ms value above is used until five bursts are recorded."
            : string.Empty;
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        CommitEditorToSelected();
        CommitMonitorAndAppSettings();
        Settings.Projects = projectItems.ToList();

        SyncAzureDraftFromUi();
        var azureCommit = await azureConnectionEditor.TryCommitAfterValidationAsync(
            AppSettingsValidator.Validate,
            CancellationToken.None);
        if (!azureCommit.Succeeded)
        {
            ToastNotificationService.ShowIfEnabled(
                "Settings not saved",
                string.Join(Environment.NewLine, azureCommit.Errors),
                ToastKind.Warning,
                UserNotificationCategory.Warning);
            return;
        }

        AzurePatBox.Password = string.Empty;
        DialogResult = true;
        Close();
    }
}
