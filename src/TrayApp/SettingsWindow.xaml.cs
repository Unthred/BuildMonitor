using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Git;
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
    private readonly AzureConnectionSecretStore azureSecretStore;
    private readonly LocalGitContextReader localGitContextReader = new();
    private readonly SettingsAzureAssociationService azureAssociationService;
    private readonly IRegisteredBrowserCatalog? registeredBrowserCatalog;

    public AppSettings Settings { get; }

    public SettingsWindow(
        AppSettings settings,
        AppWindowsLayoutStore windowsLayoutStore,
        IRegisteredBrowserCatalog? registeredBrowserCatalog = null)
    {
        this.registeredBrowserCatalog = registeredBrowserCatalog;
        this.windowsLayoutStore = windowsLayoutStore;
        InitializeComponent();
        Settings = settings;
        themeAtOpen = Settings.AppBehavior.Theme;
        azureSecretStore = new AzureConnectionSecretStore(
            AzureConnectionSecretStore.DefaultSecretsDirectory,
            new DpapiSecretProtector());
        azureConnectionEditor = new AzureConnectionSettingsEditor(
            Settings,
            azureSecretStore,
            azureDiscoveryClient);
        azureAssociationService = new SettingsAzureAssociationService(
            this,
            Settings,
            azureDiscoveryClient,
            azureSecretStore,
            localGitContextReader);

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
            SanitizePersistedTestTarget(project);
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
        LinkBrowserCombo.IsEnabled = selectedProject is not null;
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
            UpdateAzureAttachmentUi(project);
            ReloadLinkBrowserComboForProject(project);

            var local = project.Local;
            var hasLocal = local is not null;
            AzureOnlyHint.Visibility = hasLocal ? Visibility.Collapsed : Visibility.Visible;
            SetLocalEditorEnabled(hasLocal);

            if (local is not null)
            {
                SanitizePersistedTestTarget(project);
                RootFolderText.Text = local.RootFolder;
                ProjectFileText.Text = local.ProjectFile;
                ExtraArgsText.Text = local.ExtraDotNetArgs;
                RunModeCombo.SelectedItem = local.RunOptions.RunMode;
                SelectBuildControlMode(local.BuildControlMode);
                SelectPreferredSiteUrlScheme(local.PreferredSiteUrlScheme);
                StartOnLaunchCheck.IsChecked = local.StartOnLaunch;
                RestartOnCrashCheck.IsChecked = local.RunOptions.RestartOnCrash;
                MaxRetriesText.Text = local.RunOptions.MaxRestartRetries.ToString();
                AutoRestartOnWatchChangesCheck.IsChecked = local.RunOptions.AutoRestartOnWatchChanges;
                AutoRestartOnHotReloadRequestCheck.IsChecked = local.RunOptions.AutoRestartOnHotReloadRequest;
                RestartAppAfterRebuildCheck.IsChecked = local.RunOptions.RestartAppAfterRebuild;
                RunTestsCombo.SelectedItem = local.RunOptions.RunTests;
                AutoOpenLogCombo.SelectedItem = local.RunOptions.AutoOpenLog;
                ShowStatusPanelWhileBuildingCheck.IsChecked = local.RunOptions.ShowStatusPanelWhileBuilding;
                FileChangesCombo.SelectedItem = local.RunOptions.FileChanges;
                WatchExcludeSegmentsText.Text = local.RunOptions.WatchExcludeSegments;
                ReleaseOutputLocksCheck.IsChecked = local.RunOptions.ReleaseOutputLocksBeforeBuild;
                ForceCompleteWarningCountsCheck.IsChecked = local.RunOptions.ForceCompleteWarningCounts;
                AutoRepairCorruptedOutputCheck.IsChecked = local.RunOptions.AutoRepairCorruptedOutput;
                // Clear combo text before reload so prior project values cannot bleed into current.
                TestProjectCombo.ItemsSource = null;
                TestProjectCombo.Text = string.Empty;
                LaunchProfileCombo.ItemsSource = null;
                LaunchProfileCombo.Text = string.Empty;
                ReloadLaunchProfiles(selectCurrent: true);
                ReloadTestProjectCandidates(selectCurrent: true, preferModelValue: true);
                RefreshAgentSkillStatus();
                ApplyCapabilityPresentation(project);
            }
            else
            {
                RootFolderText.Text = string.Empty;
                ProjectFileText.Text = string.Empty;
                TestProjectCombo.ItemsSource = null;
                TestProjectCombo.Text = string.Empty;
                TestTargetEffectiveHint.Text = string.Empty;
                AgentSkillStatusSummary.Text = "Azure-only project";
                AgentSkillStatusDetail.Text = "Associate a local folder to enable agent skill install and local build options.";
                InstallAgentSkillButton.IsEnabled = false;
                ApplyCapabilityPresentation(project);
            }

            SelectLinkBrowserForProject(project);
        }
        finally
        {
            isLoadingEditor = false;
        }
    }

    private static void SanitizePersistedTestTarget(MonitoredProjectSettings project)
    {
        if (project.Local is null)
        {
            return;
        }

        project.Local.TestProjectFile = TestProjectPathRules.SanitizeForRoot(
            project.Local.RootFolder,
            project.Local.TestProjectFile);
    }

    private void RunModeComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingEditor || selectedProject is null)
        {
            return;
        }

        if (RunModeCombo.SelectedItem is ProjectRunMode mode && selectedProject.Local is not null)
        {
            selectedProject.Local.RunOptions.RunMode = mode;
        }

        ApplyCapabilityPresentation(selectedProject);
    }

    private void ApplyCapabilityPresentation(MonitoredProjectSettings? project)
    {
        var root = project?.Local?.RootFolder ?? RootFolderText.Text.Trim();
        var projectFile = project?.Local?.ProjectFile ?? ProjectFileText.Text.Trim();
        var profiles = string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(projectFile)
            ? Array.Empty<string>()
            : LaunchProfileDiscovery.DiscoverProfiles(root, projectFile);
        var siteUrl = !string.IsNullOrWhiteSpace(root)
                      && !string.IsNullOrWhiteSpace(projectFile)
                      && LaunchProfileDiscovery.AnyProfileHasApplicationUrl(root, projectFile);

        // Use combo RunMode when editing so visibility updates before commit.
        if (project?.Local is not null && RunModeCombo.SelectedItem is ProjectRunMode mode)
        {
            project.Local.RunOptions.RunMode = mode;
        }

        var caps = SettingsProjectCapabilityPolicy.Evaluate(
            project,
            launchProfilesAvailable: profiles.Count > 0,
            siteUrlApplicable: siteUrl);

        var launchVis = caps.LaunchProfilesAvailable ? Visibility.Visible : Visibility.Collapsed;
        var siteVis = caps.SiteUrlApplicable ? Visibility.Visible : Visibility.Collapsed;
        LaunchProfileLabel.Visibility = launchVis;
        LaunchProfileCombo.Visibility = launchVis;
        SiteUrlLabel.Visibility = siteVis;
        PreferredSiteUrlCombo.Visibility = siteVis;
        RestartOptionsPanel.Visibility = caps.RestartApplicable ? Visibility.Visible : Visibility.Collapsed;
        AutoRestartOnWatchChangesCheck.Visibility =
            caps.WatchRestartApplicable ? Visibility.Visible : Visibility.Collapsed;

        var runMode = project?.Local?.RunOptions.RunMode
                      ?? (RunModeCombo.SelectedItem as ProjectRunMode?)
                      ?? ProjectRunMode.Watch;
        var profileForContext = LaunchProfileCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(profileForContext))
        {
            profileForContext = LaunchProfileDiscovery.GetPreferredProfile(profiles) ?? string.Empty;
        }

        ApplyBuildCliContextPresentation(
            SettingsBuildCliContextPresenter.Build(
                caps,
                launchProfilesDetected: profiles.Count > 0,
                webEndpointDetected: siteUrl,
                selectedOrPreferredLaunchProfile: profileForContext,
                runMode: runMode));
    }

    private void ApplyBuildCliContextPresentation(SettingsBuildCliContextView view)
    {
        BuildCliLaunchBehaviourPanel.Visibility =
            view.ShowLaunchBehaviour ? Visibility.Visible : Visibility.Collapsed;
        BuildCliLaunchBehaviourTitle.Text = view.LaunchBehaviourTitle;
        BuildCliLaunchBehaviourBody.Text = view.LaunchBehaviourBody;

        BuildCliDetectionPanel.Visibility =
            view.ShowDetection ? Visibility.Visible : Visibility.Collapsed;
        BuildCliDetectionTitle.Text = view.DetectionTitle;
        BuildCliDetectionBody.Text = string.Join(Environment.NewLine, view.DetectionLines);
    }

    private void SetLocalEditorEnabled(bool enabled)
    {
        RootFolderText.IsEnabled = enabled;
        ProjectFileText.IsEnabled = enabled;
        ExtraArgsText.IsEnabled = enabled;
        LaunchProfileCombo.IsEnabled = enabled;
        PreferredSiteUrlCombo.IsEnabled = enabled;
        RunModeCombo.IsEnabled = enabled;
        BuildControlModeCombo.IsEnabled = enabled;
        StartOnLaunchCheck.IsEnabled = enabled;
        RestartOnCrashCheck.IsEnabled = enabled;
        MaxRetriesText.IsEnabled = enabled;
        AutoRestartOnWatchChangesCheck.IsEnabled = enabled;
        AutoRestartOnHotReloadRequestCheck.IsEnabled = enabled;
        RestartAppAfterRebuildCheck.IsEnabled = enabled;
        RunTestsCombo.IsEnabled = enabled;
        AutoOpenLogCombo.IsEnabled = enabled;
        ShowStatusPanelWhileBuildingCheck.IsEnabled = enabled;
        FileChangesCombo.IsEnabled = enabled;
        WatchExcludeSegmentsText.IsEnabled = enabled;
        ReleaseOutputLocksCheck.IsEnabled = enabled;
        ForceCompleteWarningCountsCheck.IsEnabled = enabled;
        AutoRepairCorruptedOutputCheck.IsEnabled = enabled;
        TestProjectCombo.IsEnabled = enabled;
    }

    private void ReloadLinkBrowserComboForProject(MonitoredProjectSettings project)
    {
        LinkBrowserCombo.Items.Clear();
        LinkBrowserCombo.Items.Add(new ComboBoxItem
        {
            Content = "System default",
            Tag = string.Empty
        });

        var persistedId = ProjectLinkBrowserPreferenceRules.ResolveRegisteredBrowserId(project);
        var seenPersisted = false;
        if (registeredBrowserCatalog is not null)
        {
            registeredBrowserCatalog.Refresh();
            foreach (var browser in registeredBrowserCatalog.GetBrowsers())
            {
                LinkBrowserCombo.Items.Add(new ComboBoxItem
                {
                    Content = browser.DisplayName,
                    Tag = browser.RegisteredBrowserId
                });
                if (!string.IsNullOrWhiteSpace(persistedId)
                    && string.Equals(browser.RegisteredBrowserId, persistedId, StringComparison.OrdinalIgnoreCase))
                {
                    seenPersisted = true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(persistedId) && !seenPersisted)
        {
            LinkBrowserCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{persistedId} (not installed)",
                Tag = persistedId
            });
        }
    }

    private void SelectLinkBrowserForProject(MonitoredProjectSettings project)
    {
        var persistedId = ProjectLinkBrowserPreferenceRules.ResolveRegisteredBrowserId(project);
        if (string.IsNullOrWhiteSpace(persistedId))
        {
            LinkBrowserCombo.SelectedIndex = 0;
            return;
        }

        foreach (ComboBoxItem item in LinkBrowserCombo.Items)
        {
            if (item.Tag is string tag
                && string.Equals(tag, persistedId, StringComparison.OrdinalIgnoreCase))
            {
                LinkBrowserCombo.SelectedItem = item;
                return;
            }
        }

        LinkBrowserCombo.SelectedIndex = 0;
    }

    private string? ResolveLinkBrowserRegisteredId()
    {
        if (LinkBrowserCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            return string.IsNullOrWhiteSpace(tag) ? null : tag;
        }

        return null;
    }

    private void LinkBrowserComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingEditor || selectedProject is null)
        {
            return;
        }

        selectedProject.LinkBrowserRegisteredId = ResolveLinkBrowserRegisteredId();
    }

    private void UpdateAzureAttachmentUi(MonitoredProjectSettings project)
    {
        if (project.Azure is null)
        {
            AzureAttachmentSummary.Text =
                "No Azure association. Attach Azure DevOps to poll selected pipelines for this project.";
            AttachAzureButton.Visibility = project.Local is not null ? Visibility.Visible : Visibility.Collapsed;
            ChangeAzureButton.Visibility = Visibility.Collapsed;
            DetachAzureButton.Visibility = Visibility.Collapsed;
            AssociateLocalButton.Visibility = project.Local is null ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var azure = project.Azure;
        var pipeCount = azure.Pipelines.Count;
        var pipeLabel = pipeCount == 0
            ? "Connected / Not monitored (0 pipelines)"
            : $"{pipeCount} pipeline(s) selected";
        AzureAttachmentSummary.Text =
            $"{azure.AdoProjectName} / {azure.RepositoryName} — {pipeLabel}"
            + (string.IsNullOrWhiteSpace(azure.DefaultBranch) ? string.Empty : $"; default branch {azure.DefaultBranch}");
        AttachAzureButton.Visibility = Visibility.Collapsed;
        ChangeAzureButton.Visibility = Visibility.Visible;
        DetachAzureButton.Visibility = Visibility.Visible;
        AssociateLocalButton.Visibility = project.Local is null ? Visibility.Visible : Visibility.Collapsed;
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

    private void ReloadTestProjectCandidates(bool selectCurrent, bool preferModelValue = false)
    {
        var root = RootFolderText.Text.Trim();
        var projectFile = ProjectFileText.Text.Trim();
        var absoluteCandidates = TestProjectDiscovery.DiscoverCandidates(root, projectFile);
        var candidates = absoluteCandidates
            .Select(path => LaunchProfileDiscovery.ToRelativePath(root, path))
            .ToList();

        // Prefer model when loading a project. Preferring combo Text caused cross-project bleed:
        // WitherbyConnect's test path remained in the ComboBox and was written onto BuildMonitor.
        string? current;
        if (preferModelValue || !selectCurrent)
        {
            current = selectedProject?.Local?.TestProjectFile;
        }
        else
        {
            current = TestProjectCombo.Text.Trim().Length > 0
                ? TestProjectCombo.Text.Trim()
                : selectedProject?.Local?.TestProjectFile;
        }

        current = TestProjectPathRules.SanitizeForRoot(root, current);

        TestProjectCombo.ItemsSource = candidates;
        TestTargetEffectiveHint.Text = string.Empty;

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

            return;
        }

        TestProjectCombo.Text = string.Empty;
        TestProjectCombo.SelectedItem = null;
        var resolution = TestProjectDiscovery.Resolve(root, projectFile, null);
        if (resolution.AutoDiscovered && resolution.Targets.Count >= 1)
        {
            var relative = LaunchProfileDiscovery.ToRelativePath(root, resolution.Targets[0]);
            TestTargetEffectiveHint.Text = $"Auto-detects: {relative}";
            if (selectedProject is not null && preferModelValue)
            {
                EnsureLocal(selectedProject).TestProjectFile = string.Empty;
            }
        }
        else
        {
            TestTargetEffectiveHint.Text = resolution.DiscoveryNote;
        }
    }

    private void CommitEditorToSelected()
    {
        if (selectedProject is null)
        {
            return;
        }

        selectedProject.DisplayName = DisplayNameText.Text.Trim();
        selectedProject.LinkBrowserRegisteredId = ResolveLinkBrowserRegisteredId();
        if (selectedProject.Local is null)
        {
            return;
        }

        var local = selectedProject.Local;
        local.RootFolder = RootFolderText.Text.Trim();
        local.ProjectFile = ProjectFileText.Text.Trim();
        if (LaunchProfileCombo.Visibility == Visibility.Visible)
        {
            local.LaunchProfile = LaunchProfileCombo.Text.Trim();
        }

        local.TestProjectFile = TestProjectPathRules.SanitizeForRoot(
            local.RootFolder,
            TestProjectCombo.Text.Trim());
        local.ExtraDotNetArgs = ExtraArgsText.Text.Trim();
        local.RunOptions.RunMode = (ProjectRunMode)(RunModeCombo.SelectedItem ?? ProjectRunMode.Watch);
        local.BuildControlMode = ResolveBuildControlMode();
        if (PreferredSiteUrlCombo.Visibility == Visibility.Visible)
        {
            local.PreferredSiteUrlScheme = ResolvePreferredSiteUrlScheme();
        }

        local.StartOnLaunch = StartOnLaunchCheck.IsChecked == true;
        if (RestartOptionsPanel.Visibility == Visibility.Visible)
        {
            local.RunOptions.RestartOnCrash = RestartOnCrashCheck.IsChecked == true;
            if (int.TryParse(MaxRetriesText.Text, out var retries))
            {
                local.RunOptions.MaxRestartRetries = retries;
            }

            local.RunOptions.AutoRestartOnHotReloadRequest = AutoRestartOnHotReloadRequestCheck.IsChecked == true;
            local.RunOptions.RestartAppAfterRebuild = RestartAppAfterRebuildCheck.IsChecked == true;
            if (AutoRestartOnWatchChangesCheck.Visibility == Visibility.Visible)
            {
                local.RunOptions.AutoRestartOnWatchChanges = AutoRestartOnWatchChangesCheck.IsChecked == true;
            }
        }

        local.RunOptions.RunTests = (TestRunTrigger)(RunTestsCombo.SelectedItem ?? TestRunTrigger.Off);
        local.RunOptions.AutoOpenLog = (AutoOpenLogMode)(AutoOpenLogCombo.SelectedItem ?? AutoOpenLogMode.Never);
        local.RunOptions.ShowStatusPanelWhileBuilding = ShowStatusPanelWhileBuildingCheck.IsChecked == true;
        local.RunOptions.FileChanges = (FileChangeMode)(FileChangesCombo.SelectedItem ?? FileChangeMode.WatchOnly);
        local.RunOptions.WatchExcludeSegments = WatchExcludeSegmentsText.Text.Trim();
        local.RunOptions.ReleaseOutputLocksBeforeBuild = ReleaseOutputLocksCheck.IsChecked == true;
        local.RunOptions.ForceCompleteWarningCounts = ForceCompleteWarningCountsCheck.IsChecked == true;
        local.RunOptions.AutoRepairCorruptedOutput = AutoRepairCorruptedOutputCheck.IsChecked == true;
    }

    private void AddProjectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void AddLocalProjectClicked(object sender, RoutedEventArgs e)
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

    private async void AddFromAzureClicked(object sender, RoutedEventArgs e)
    {
        CommitEditorToSelected();
        var project = await azureAssociationService.TryAddFromAzureAsync();
        if (project is null)
        {
            return;
        }

        projectItems.Add(project);
        ProjectsList.SelectedItem = project;
    }

    private async void AttachAzureClicked(object sender, RoutedEventArgs e)
    {
        if (selectedProject is null)
        {
            return;
        }

        CommitEditorToSelected();
        if (await azureAssociationService.TryAttachAsync(selectedProject))
        {
            UpdateAzureAttachmentUi(selectedProject);
        }
    }

    private async void ChangeAzureClicked(object sender, RoutedEventArgs e)
    {
        if (selectedProject is null)
        {
            return;
        }

        CommitEditorToSelected();
        if (await azureAssociationService.TryChangeAsync(selectedProject))
        {
            UpdateAzureAttachmentUi(selectedProject);
        }
    }

    private void DetachAzureClicked(object sender, RoutedEventArgs e)
    {
        if (selectedProject is null)
        {
            return;
        }

        if (!azureAssociationService.TryDetach(selectedProject, out var error))
        {
            System.Windows.MessageBox.Show(this, error, "Cannot detach Azure", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        UpdateAzureAttachmentUi(selectedProject);
    }

    private void AssociateLocalClicked(object sender, RoutedEventArgs e)
    {
        if (selectedProject is null)
        {
            return;
        }

        if (!azureAssociationService.TryAssociateLocalFolder(selectedProject))
        {
            return;
        }

        DisplayNameText.Text = selectedProject.DisplayName;
        LoadEditorFromProject(selectedProject);
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
