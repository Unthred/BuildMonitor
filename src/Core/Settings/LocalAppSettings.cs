using System.ComponentModel;
using System.Runtime.CompilerServices;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 4;
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
    /// <summary>Path segments ignored by file watcher (semicolon-separated). Default includes IDE folders.</summary>
    public string WatchExcludeSegments { get; set; } =
        ".cursor;agent-transcripts;terminals;mcps;.specstory;plans;.idea;.vscode";
}

public sealed class GlobalMonitorSettings
{
    public int HealthRefreshSeconds { get; set; } = 5;
    /// <summary>Quiet period after the last file change before a coalesced rebuild starts.</summary>
    public int FileChangeDebounceMs { get; set; } = 3000;
    /// <summary>When watch mode is enabled, batch file changes and rebuild once edits settle (instead of dotnet watch per-save rebuilds).</summary>
    public bool CoalesceWatchRebuilds { get; set; } = true;
    public int MaxConcurrentActiveProjects { get; set; } = 3;
    public bool AutoOpenLogOnFailure { get; set; }
    public bool PlaySoundOnBuildError { get; set; } = true;
    public bool PlaySoundOnBuildSuccess { get; set; }
    public int MaxLogDisplayBytes { get; set; } = 2_097_152;
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

public sealed class AppBehaviorSettings
{
    public bool RunOnLogon { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;
    public ToastPosition ToastPosition { get; set; } = ToastPosition.BottomRight;
    public int ToastDurationSeconds { get; set; } = 7;
    public ToastNotificationSettings Toasts { get; set; } = new();
}
