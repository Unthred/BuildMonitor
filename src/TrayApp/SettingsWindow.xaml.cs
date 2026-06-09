using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.TrayApp.Services;
using Microsoft.Win32;

namespace BuildMonitor.TrayApp;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<LocalProjectDefinition> projectItems = [];
    private LocalProjectDefinition? selectedProject;
    private bool isLoadingEditor;
    private readonly AppThemePreference themeAtOpen;

    public AppSettings Settings { get; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        themeAtOpen = Settings.AppBehavior.Theme;

        RunModeCombo.ItemsSource = Enum.GetValues<ProjectRunMode>();
        RunTestsCombo.ItemsSource = Enum.GetValues<TestRunTrigger>();
        FileChangesCombo.ItemsSource = Enum.GetValues<FileChangeMode>();
        ThemeCombo.ItemsSource = Enum.GetValues<AppThemePreference>();
        ThemeCombo.SelectedItem = Settings.AppBehavior.Theme;
        ToastPositionCombo.ItemsSource = Enum.GetValues<ToastPosition>();
        ToastPositionCombo.SelectedItem = Settings.AppBehavior.ToastPosition;
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
        HealthRefreshText.Text = Settings.Monitor.HealthRefreshSeconds.ToString();
        AutoOpenLogCheck.IsChecked = Settings.Monitor.AutoOpenLogOnFailure;
        PlaySoundOnErrorCheck.IsChecked = Settings.Monitor.PlaySoundOnBuildError;
        PlaySoundOnSuccessCheck.IsChecked = Settings.Monitor.PlaySoundOnBuildSuccess;
        MaxLogBytesText.Text = Settings.Monitor.MaxLogDisplayBytes.ToString();
        StartMinimizedCheck.IsChecked = Settings.AppBehavior.StartMinimizedToTray;
        RunOnLogonCheck.IsChecked = Settings.AppBehavior.RunOnLogon;

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
        if (DialogResult != true)
        {
            ThemeService.ApplyTheme(themeAtOpen);
        }

        base.OnClosing(e);
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        var theme = ThemeService.Resolve(Settings.AppBehavior.Theme);
        ThemeService.ApplyToWindow(this, theme);
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
        selectedProject = ProjectsList.SelectedItem as LocalProjectDefinition;
        EditorPanel.IsEnabled = selectedProject is not null;
        if (selectedProject is null)
        {
            return;
        }

        LoadEditorFromProject(selectedProject);
    }

    private void LoadEditorFromProject(LocalProjectDefinition project)
    {
        isLoadingEditor = true;
        try
        {
            DisplayNameText.Text = project.DisplayName;
            RootFolderText.Text = project.RootFolder;
            ProjectFileText.Text = project.ProjectFile;
            ExtraArgsText.Text = project.ExtraDotNetArgs;
            RunModeCombo.SelectedItem = project.RunOptions.RunMode;
            RestartOnCrashCheck.IsChecked = project.RunOptions.RestartOnCrash;
            MaxRetriesText.Text = project.RunOptions.MaxRestartRetries.ToString();
            AutoRestartOnWatchChangesCheck.IsChecked = project.RunOptions.AutoRestartOnWatchChanges;
            RestartAppAfterRebuildCheck.IsChecked = project.RunOptions.RestartAppAfterRebuild;
            RunTestsCombo.SelectedItem = project.RunOptions.RunTests;
            FileChangesCombo.SelectedItem = project.RunOptions.FileChanges;
            ReleaseOutputLocksCheck.IsChecked = project.RunOptions.ReleaseOutputLocksBeforeBuild;
            ReloadLaunchProfiles(selectCurrent: true);
            ReloadTestProjectCandidates(selectCurrent: true);
        }
        finally
        {
            isLoadingEditor = false;
        }
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

        selectedProject.TestProjectFile = TestProjectCombo.Text.Trim();
    }

    private void LaunchProfileComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingEditor || selectedProject is null || LaunchProfileCombo.SelectedItem is null)
        {
            return;
        }

        selectedProject.LaunchProfile = LaunchProfileCombo.SelectedItem.ToString() ?? string.Empty;
    }

    private void ReloadLaunchProfiles(bool selectCurrent)
    {
        var profiles = LaunchProfileDiscovery.DiscoverProfiles(
            RootFolderText.Text.Trim(),
            ProjectFileText.Text.Trim());

        var current = selectCurrent
            ? (LaunchProfileCombo.Text.Trim().Length > 0 ? LaunchProfileCombo.Text.Trim() : selectedProject?.LaunchProfile)
            : selectedProject?.LaunchProfile;

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
                selectedProject.LaunchProfile = preferred;
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
            ? (TestProjectCombo.Text.Trim().Length > 0 ? TestProjectCombo.Text.Trim() : selectedProject?.TestProjectFile)
            : selectedProject?.TestProjectFile;

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
                    selectedProject.TestProjectFile = string.Empty;
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
        selectedProject.RootFolder = RootFolderText.Text.Trim();
        selectedProject.ProjectFile = ProjectFileText.Text.Trim();
        selectedProject.LaunchProfile = LaunchProfileCombo.Text.Trim();
        selectedProject.TestProjectFile = TestProjectCombo.Text.Trim();
        selectedProject.ExtraDotNetArgs = ExtraArgsText.Text.Trim();
        selectedProject.RunOptions.RunMode = (ProjectRunMode)(RunModeCombo.SelectedItem ?? ProjectRunMode.Watch);
        selectedProject.RunOptions.RestartOnCrash = RestartOnCrashCheck.IsChecked == true;
        if (int.TryParse(MaxRetriesText.Text, out var retries))
        {
            selectedProject.RunOptions.MaxRestartRetries = retries;
        }

        selectedProject.RunOptions.AutoRestartOnWatchChanges = AutoRestartOnWatchChangesCheck.IsChecked == true;
        selectedProject.RunOptions.RestartAppAfterRebuild = RestartAppAfterRebuildCheck.IsChecked == true;

        selectedProject.RunOptions.RunTests = (TestRunTrigger)(RunTestsCombo.SelectedItem ?? TestRunTrigger.Off);
        selectedProject.RunOptions.FileChanges = (FileChangeMode)(FileChangesCombo.SelectedItem ?? FileChangeMode.WatchOnly);
        selectedProject.RunOptions.ReleaseOutputLocksBeforeBuild = ReleaseOutputLocksCheck.IsChecked == true;
    }

    private void AddProjectClicked(object sender, RoutedEventArgs e)
    {
        CommitEditorToSelected();
        var project = new LocalProjectDefinition
        {
            DisplayName = "New project",
            RootFolder = Environment.CurrentDirectory
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
                selectedProject.RootFolder = dialog.FolderName;
            }

            ReloadLaunchProfiles(selectCurrent: true);
            ReloadTestProjectCandidates(selectCurrent: true);
        }
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
            selectedProject.ProjectFile = ProjectFileText.Text;
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

        if (int.TryParse(HealthRefreshText.Text, out var refresh))
        {
            Settings.Monitor.HealthRefreshSeconds = refresh;
        }

        Settings.Monitor.AutoOpenLogOnFailure = AutoOpenLogCheck.IsChecked == true;
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

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        CommitEditorToSelected();
        CommitMonitorAndAppSettings();
        Settings.Projects = projectItems.ToList();

        var errors = AppSettingsValidator.Validate(Settings);
        if (errors.Count > 0)
        {
            ToastNotificationService.ShowIfEnabled(
                "Settings not saved",
                string.Join(Environment.NewLine, errors),
                ToastKind.Warning,
                UserNotificationCategory.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
