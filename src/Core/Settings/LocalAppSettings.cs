using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 22;
    /// <summary>Azure DevOps org connections (credential references live outside settings.json).</summary>
    public List<AzureDevOpsConnectionSettings> Connections { get; set; } = [];
    public List<MonitoredProjectSettings> Projects { get; set; } = [];
    public GlobalMonitorSettings Monitor { get; set; } = new();
    public AppBehaviorSettings AppBehavior { get; set; } = new();
}

/// <summary>Top-level Azure DevOps organisation connection. PATs are not stored here.</summary>
public sealed class AzureDevOpsConnectionSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string OrganizationUrl { get; set; } = string.Empty;
}

/// <summary>Logical BuildMonitor project with optional Local and/or Azure attachments.</summary>
public sealed class MonitoredProjectSettings : INotifyPropertyChanged
{
    private string displayName = string.Empty;
    private bool isActiveInSession;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName
    {
        get => displayName;
        set
        {
            if (displayName == value)
            {
                return;
            }

            displayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ListLabel));
        }
    }

    public string ListLabel => string.IsNullOrWhiteSpace(DisplayName) ? "(unnamed)" : DisplayName;

    public bool IsActiveInSession
    {
        get => isActiveInSession;
        set => SetField(ref isActiveInSession, value);
    }

    public LocalProjectAttachment? Local { get; set; }

    public AzureDevOpsProjectAttachment? Azure { get; set; }

    /// <summary>
    /// Windows StartMenuInternet ProgId for link navigation; null/empty = system default.
    /// Omitted from settings.json when unset (no migration materialization).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LinkBrowserRegisteredId { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Local build/run/watch/test attachment for a monitored project.</summary>
public sealed class LocalProjectAttachment : INotifyPropertyChanged
{
    private string rootFolder = string.Empty;
    private string projectFile = string.Empty;
    private string launchProfile = string.Empty;
    private string extraDotNetArgs = string.Empty;
    private string testProjectFile = string.Empty;
    private bool startOnLaunch = true;
    private ProjectBuildControlMode buildControlMode = ProjectBuildControlMode.FileWatching;
    private PreferredSiteUrlScheme preferredSiteUrlScheme = PreferredSiteUrlScheme.Auto;

    public string RootFolder
    {
        get => rootFolder;
        set => SetField(ref rootFolder, value);
    }

    public string ProjectFile
    {
        get => projectFile;
        set => SetField(ref projectFile, value);
    }

    public string LaunchProfile
    {
        get => launchProfile;
        set => SetField(ref launchProfile, value);
    }

    public string ExtraDotNetArgs
    {
        get => extraDotNetArgs;
        set => SetField(ref extraDotNetArgs, value);
    }

    /// <summary>Optional .sln/.slnx or test .csproj. Empty = auto-detect from repo root.</summary>
    public string TestProjectFile
    {
        get => testProjectFile;
        set => SetField(ref testProjectFile, value);
    }

    /// <summary>When true and the project is active in session, build/run starts automatically on launch or settings save.</summary>
    public bool StartOnLaunch
    {
        get => startOnLaunch;
        set => SetField(ref startOnLaunch, value);
    }

    public ProjectBuildControlMode BuildControlMode
    {
        get => buildControlMode;
        set => SetField(ref buildControlMode, value);
    }

    public PreferredSiteUrlScheme PreferredSiteUrlScheme
    {
        get => preferredSiteUrlScheme;
        set => SetField(ref preferredSiteUrlScheme, value);
    }

    public ProjectRunOptions RunOptions { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Repository-centric Azure DevOps attachment. Zero pipelines means Connected / Not monitored.</summary>
public sealed class AzureDevOpsProjectAttachment
{
    public string ConnectionId { get; set; } = string.Empty;
    public string AdoProjectId { get; set; } = string.Empty;
    public string AdoProjectName { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string? RepositoryRemoteUrl { get; set; }
    /// <summary>Last-known default branch from Azure (normalized short name). Not a manual override.</summary>
    public string? DefaultBranch { get; set; }
    public List<string> ExtraWatchedBranches { get; set; } = [];
    public List<AzurePipelineSelection> Pipelines { get; set; } = [];
}

public sealed class AzurePipelineSelection
{
    public int DefinitionId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public List<string> IncludedBranches { get; set; } = [];
    public NotificationMode NotificationMode { get; set; } = NotificationMode.FailuresAndRecovery;
    public int Priority { get; set; }
}

public sealed class ProjectRunOptions
{
    public ProjectRunMode RunMode { get; set; } = ProjectRunMode.Watch;
    public bool RestartOnCrash { get; set; } = true;
    public int MaxRestartRetries { get; set; } = 5;
    public bool AutoRestartOnWatchChanges { get; set; } = true;
    public bool AutoRestartOnHotReloadRequest { get; set; } = true;
    public bool RestartAppAfterRebuild { get; set; } = true;
    public TestRunTrigger RunTests { get; set; } = TestRunTrigger.Off;
    public FileChangeMode FileChanges { get; set; } = FileChangeMode.WatchOnly;
    public bool ReleaseOutputLocksBeforeBuild { get; set; }
    public bool ForceCompleteWarningCounts { get; set; } = true;
    public bool AutoRepairCorruptedOutput { get; set; } = true;
    public string WatchExcludeSegments { get; set; } =
        ".cursor;agent-transcripts;terminals;mcps;.specstory;plans;.idea;.vscode;docs;templates;.github";
    public AutoOpenLogMode AutoOpenLog { get; set; } = AutoOpenLogMode.Never;
    public bool ShowStatusPanelWhileBuilding { get; set; } = true;
}

public enum FileChangeDebounceMode
{
    Manual = 0,
    Auto = 1
}

public sealed class GlobalMonitorSettings
{
    public int HealthRefreshSeconds { get; set; } = 5;
    public int FileChangeDebounceMs { get; set; } = 3000;
    public FileChangeDebounceMode FileChangeDebounceMode { get; set; } = FileChangeDebounceMode.Manual;
    public bool CoalesceWatchRebuilds { get; set; } = true;
    public int MaxConcurrentActiveProjects { get; set; } = 3;
    [Obsolete("Migrated to per-project ProjectRunOptions.AutoOpenLog (schema v11).")]
    public bool AutoOpenLogOnFailure { get; set; }
    public bool AutoOpenBuildMonitorHealthOnStartup { get; set; } = true;
    public bool PlaySoundOnBuildError { get; set; } = true;
    public bool PlaySoundOnBuildSuccess { get; set; }
    public int MaxLogDisplayBytes { get; set; } = 2_097_152;
    public bool DeferStartupBuildUntilQuiet { get; set; } = true;
    public bool CancelSupersededBuilds { get; set; } = true;
    public bool UseAgentTranscriptActivity { get; set; } = true;
    public bool LearnFromDiagnosticsVerdicts { get; set; } = true;
    public bool ControlPlaneEnabled { get; set; } = true;
    public int ControlPlanePort { get; set; } = 7700;
    public int ControlPlaneBusyTimeoutSeconds { get; set; } = 120;
    public bool SuppressAutoBuildTests { get; set; } = true;
}

public enum AppThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum ToastPosition
{
    BottomRight = 0,
    BottomLeft = 1,
    TopRight = 2,
    TopLeft = 3
}

public sealed class ToastNotificationSettings
{
    public bool BuildStart { get; set; }
    public bool BuildSuccess { get; set; } = true;
    public bool BuildFailure { get; set; } = true;
    public bool FileChangeDetected { get; set; } = true;
    public bool Warnings { get; set; } = true;
    public bool Errors { get; set; } = true;
    public bool Info { get; set; }
}

public enum TrayMenuLayout
{
    ByOperation = 0,
    ByProject = 1
}

public sealed class AppBehaviorSettings
{
    public bool RunOnLogon { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;
    public ToastPosition ToastPosition { get; set; } = ToastPosition.BottomRight;
    public int ToastDurationSeconds { get; set; } = 7;
    public TrayMenuLayout TrayMenuLayout { get; set; } = TrayMenuLayout.ByOperation;
    public ToastNotificationSettings Toasts { get; set; } = new();
    public bool FollowStatusPanelToVirtualDesktop { get; set; } = true;
    public bool FollowBuildLogToVirtualDesktop { get; set; } = true;
}

/// <summary>
/// Flat project shape used only when loading schema ≤20 JSON before nesting under <see cref="MonitoredProjectSettings.Local"/>.
/// </summary>
public sealed class LegacyFlatProjectSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string RootFolder { get; set; } = string.Empty;
    public string ProjectFile { get; set; } = string.Empty;
    public string LaunchProfile { get; set; } = string.Empty;
    public string ExtraDotNetArgs { get; set; } = string.Empty;
    public string TestProjectFile { get; set; } = string.Empty;
    public bool IsActiveInSession { get; set; }
    public bool StartOnLaunch { get; set; } = true;
    public ProjectBuildControlMode BuildControlMode { get; set; } = ProjectBuildControlMode.FileWatching;
    public PreferredSiteUrlScheme PreferredSiteUrlScheme { get; set; } = PreferredSiteUrlScheme.Auto;
    public ProjectRunOptions RunOptions { get; set; } = new();
}

public sealed class LegacyAppSettingsV20
{
    public int SchemaVersion { get; set; } = 20;
    public List<LegacyFlatProjectSettings> Projects { get; set; } = [];
    public GlobalMonitorSettings Monitor { get; set; } = new();
    public AppBehaviorSettings AppBehavior { get; set; } = new();
}
