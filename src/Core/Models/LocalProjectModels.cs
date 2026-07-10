namespace BuildMonitor.Core.Models;

public enum ProjectRunMode
{
    None = 0,
    Run = 1,
    Watch = 2
}

public enum TestRunTrigger
{
    Off = 0,
    OnBuildSuccess = 1,
    OnFileChange = 2
}

public enum FileChangeMode
{
    Off = 0,
    TriggerRebuild = 1,
    WatchOnly = 2
}

public enum ProjectLifecycleState
{
    Idle = 0,
    Building = 1,
    BuildFailed = 2,
    BuildOk = 3,
    Running = 4,
    Watching = 5,
    Crashed = 6,
    Testing = 7,
    TestFailed = 8,
    TestOk = 9,
    /// <summary>Waiting for source edits to settle before building.</summary>
    WaitingForEdits = 10
}

public enum BuildLogKind
{
    Build = 0,
    Test = 1,
    WatchCompile = 2,
    Run = 3
}

public enum UserNotificationKind
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public enum UserNotificationCategory
{
    BuildStart = 0,
    BuildSuccess = 1,
    BuildFailure = 2,
    Warning = 3,
    Error = 4,
    Info = 5,
    FileChangeDetected = 6
}

public sealed record BuildLogRecord(
    string ProjectId,
    BuildLogKind Kind,
    string CommandLine,
    int ExitCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string LogFilePath,
    int ErrorCount,
    IReadOnlyList<string> ErrorLines,
    int WarningCount = 0);

public sealed record LiveBuildLogView(
    string Text,
    bool IsLive,
    ProjectLifecycleState State,
    int ErrorCount,
    int WarningCount,
    int Revision);

public sealed record ProjectHealthSnapshot(
    string ProjectId,
    string DisplayName,
    MonitorHealth Health,
    string HealthLabel,
    ProjectLifecycleState State,
    int? LastExitCode,
    TimeSpan? LastDuration,
    string? LastErrorPreview,
    int ErrorCount,
    int WarningCount,
    DateTimeOffset LastChangedUtc,
    DateTimeOffset? LastBuildFinishedAtUtc,
    bool IsActive,
    IReadOnlyList<BuildProgressStep> ProgressSteps,
    string? ListenUrl = null,
    bool ListenUrlReady = false,
    bool SupportsAppRestart = false,
    string? IssueCountsText = null,
    string? FailurePhase = null,
    bool IsRestarting = false,
    bool IsEditGatingActive = false,
    string? EditGatingDetailText = null,
    DateTimeOffset? RebuildQuietUntilUtc = null);

public enum BuildTriggerKind
{
    SessionStart = 0,
    ManualRebuild = 1,
    FileWatcher = 2,
    FileWatcherQueued = 3,
    RebuildAndRestart = 4,
    HotReloadRebuild = 5,
    HotReloadRestart = 6,
    DotNetWatchCompile = 7,
    DotNetWatchFileChange = 8,
    Other = 9,
    EditActivitySample = 10
}

public enum BuildTriggerVerdict
{
    Unreviewed = 0,
    Expected = 1,
    Unexpected = 2
}

public sealed record BuildTriggerRecord(
    string Id,
    string ProjectId,
    string ProjectDisplayName,
    DateTimeOffset OccurredAtUtc,
    BuildTriggerKind Kind,
    string Summary,
    string? Detail = null,
    IReadOnlyList<string>? ChangedPaths = null,
    BuildTriggerVerdict Verdict = BuildTriggerVerdict.Unreviewed,
    string? InferredCause = null,
    string? UserNote = null);
