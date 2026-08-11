using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 17;
    public List<LocalProjectDefinition> Projects { get; set; } = [];
    public GlobalMonitorSettings Monitor { get; set; } = new();
    public AppBehaviorSettings AppBehavior { get; set; } = new();
}

public sealed class LocalProjectDefinition : INotifyPropertyChanged
{
    private string displayName = string.Empty;
    private string rootFolder = string.Empty;
    private string projectFile = string.Empty;
    private string launchProfile = string.Empty;
    private string extraDotNetArgs = string.Empty;
    private string testProjectFile = string.Empty;
    private bool isActiveInSession;
    private bool startOnLaunch = true;

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

    public bool IsActiveInSession
    {
        get => isActiveInSession;
        set => SetField(ref isActiveInSession, value);
    }

    /// <summary>When true and active in session, build/run starts automatically when the app launches or settings are saved.</summary>
    public bool StartOnLaunch
    {
        get => startOnLaunch;
        set => SetField(ref startOnLaunch, value);
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

public sealed class ProjectRunOptions
{
    public ProjectRunMode RunMode { get; set; } = ProjectRunMode.Watch;
    public bool RestartOnCrash { get; set; } = true;
    public int MaxRestartRetries { get; set; } = 5;
    /// <summary>When watch mode detects source changes, restart without prompting (tray has no stdin).</summary>
    public bool AutoRestartOnWatchChanges { get; set; } = true;
    /// <summary>When build/run output says hot reload needs a restart or rebuild, act automatically.</summary>
    public bool AutoRestartOnHotReloadRequest { get; set; } = true;
    /// <summary>After a manual or file-triggered rebuild, start run/watch again if it was running.</summary>
    public bool RestartAppAfterRebuild { get; set; } = true;
    public TestRunTrigger RunTests { get; set; } = TestRunTrigger.Off;
    public FileChangeMode FileChanges { get; set; } = FileChangeMode.WatchOnly;
    public bool ReleaseOutputLocksBeforeBuild { get; set; }
    /// <summary>
    /// When true, every build passes <c>--no-incremental</c> so MSBuild re-emits the full warning/error summary.
    /// When false, only startup / Rebuild / Rebuild &amp; restart force a full compile (file-change builds may report 0/0).
    /// </summary>
    public bool ForceCompleteWarningCounts { get; set; } = true;
    /// <summary>When build output indicates a poisoned artifacts/bin/obj tree, stop, clean output folders, and retry.</summary>
    public bool AutoRepairCorruptedOutput { get; set; } = true;
    /// <summary>Path segments ignored by file watcher (semicolon-separated). Default includes IDE folders.</summary>
    public string WatchExcludeSegments { get; set; } =
        ".cursor;agent-transcripts;terminals;mcps;.specstory;plans;.idea;.vscode;docs;templates;.github";
    /// <summary>When to open the log viewer automatically after builds or tests.</summary>
    public AutoOpenLogMode AutoOpenLog { get; set; } = AutoOpenLogMode.Never;
    /// <summary>When true, open the hover status panel when a build starts and hide it when the build finishes.</summary>
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
    /// <summary>Quiet period after the last file change before a coalesced rebuild starts.</summary>
    public int FileChangeDebounceMs { get; set; } = 3000;
    public FileChangeDebounceMode FileChangeDebounceMode { get; set; } = FileChangeDebounceMode.Manual;
    /// <summary>When watch mode is enabled, batch file changes and rebuild once edits settle (instead of dotnet watch per-save rebuilds).</summary>
    public bool CoalesceWatchRebuilds { get; set; } = true;
    public int MaxConcurrentActiveProjects { get; set; } = 3;
    [Obsolete("Migrated to per-project ProjectRunOptions.AutoOpenLog (schema v11).")]
    public bool AutoOpenLogOnFailure { get; set; }
    /// <summary>Open the Build Monitor Health window when the app starts.</summary>
    public bool AutoOpenBuildMonitorHealthOnStartup { get; set; } = true;
    public bool PlaySoundOnBuildError { get; set; } = true;
    public bool PlaySoundOnBuildSuccess { get; set; }
    public int MaxLogDisplayBytes { get; set; } = 2_097_152;
    /// <summary>Wait for edit quiet before the first startup build.</summary>
    public bool DeferStartupBuildUntilQuiet { get; set; } = true;
    /// <summary>Cancel startup/file-change builds when newer saves arrive.</summary>
    public bool CancelSupersededBuilds { get; set; } = true;
    /// <summary>Treat agent-transcripts / .cursor writes as active editing.</summary>
    public bool UseAgentTranscriptActivity { get; set; } = true;
    /// <summary>Learn from Unexpected verdicts in Build diagnostics (exclude suggestions and debounce feedback).</summary>
    public bool LearnFromDiagnosticsVerdicts { get; set; } = true;
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
    /// <summary>Move the hover status panel onto the virtual desktop you are viewing when it opens.</summary>
    public bool FollowStatusPanelToVirtualDesktop { get; set; } = true;
    /// <summary>Move the build log window onto the virtual desktop you are viewing when it opens or is activated.</summary>
    public bool FollowBuildLogToVirtualDesktop { get; set; } = true;
}
