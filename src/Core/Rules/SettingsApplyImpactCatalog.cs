using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Exhaustive map of persisted <see cref="AppSettings"/> leaf paths → apply impact.
/// Adding a persisted property without an entry here fails the coverage test.
/// </summary>
public static class SettingsApplyImpactCatalog
{
    /// <param name="Path">
    /// Dot path under <see cref="AppSettings"/>. Collections use <c>[]</c>
    /// (e.g. <c>Projects[].Local.RootFolder</c>).
    /// </param>
    public sealed record Entry(string Path, SettingsApplyImpact Impact, string Rationale);

    public static IReadOnlyList<Entry> All { get; } =
    [
        // --- root ---
        new("SchemaVersion", SettingsApplyImpact.None,
            "Schema marker only; not a runtime input."),

        // --- connections (Azure org; PAT is outside settings.json) ---
        new("Connections[].Id", SettingsApplyImpact.SoftRuntime,
            "Connection identity; Azure poller refresh only."),
        new("Connections[].DisplayName", SettingsApplyImpact.SoftRuntime,
            "Label for Azure UI; no Local process."),
        new("Connections[].OrganizationUrl", SettingsApplyImpact.SoftRuntime,
            "Azure discovery/poll target; no Local rebuild."),

        // --- projects ---
        new("Projects[].Id", SettingsApplyImpact.HardRestart,
            "ProjectRuntime dictionary is keyed by Id; renaming Id invalidates the live runtime mapping."),
        new("Projects[].DisplayName", SettingsApplyImpact.SoftRuntime,
            "UI label only."),
        new("Projects[].IsActiveInSession", SettingsApplyImpact.HardRestart,
            "Activating/deactivating a Local project must start/stop its runtime. " +
            "Azure-only active toggles are applied via SoftRuntime when Local is null " +
            "(see classifier Local vs Soft fingerprints)."),

        // --- Local attachment (build/run defining) ---
        new("Projects[].Local.RootFolder", SettingsApplyImpact.HardRestart,
            "Changes watched/build working directory."),
        new("Projects[].Local.ProjectFile", SettingsApplyImpact.HardRestart,
            "Changes which project/solution is built."),
        new("Projects[].Local.LaunchProfile", SettingsApplyImpact.HardRestart,
            "Changes run environment / launch profile."),
        new("Projects[].Local.ExtraDotNetArgs", SettingsApplyImpact.HardRestart,
            "Changes CLI args for build/run."),
        new("Projects[].Local.TestProjectFile", SettingsApplyImpact.HardRestart,
            "Changes test target."),
        new("Projects[].Local.StartOnLaunch", SettingsApplyImpact.HardRestart,
            "Controls whether StartActive after apply should build/start."),
        new("Projects[].Local.BuildControlMode", SettingsApplyImpact.HardRestart,
            "File Watching vs AI Controlled changes auto-build policy for the live runtime."),
        new("Projects[].Local.PreferredSiteUrlScheme", SettingsApplyImpact.SoftRuntime,
            "Status URL preference only; no process restart required."),

        new("Projects[].Local.RunOptions.RunMode", SettingsApplyImpact.HardRestart,
            "Watch/Run/None changes the live child process model."),
        new("Projects[].Local.RunOptions.RestartOnCrash", SettingsApplyImpact.HardRestart,
            "Crash-restart policy is owned by the running runtime."),
        new("Projects[].Local.RunOptions.MaxRestartRetries", SettingsApplyImpact.HardRestart,
            "Restart budget for the live run host."),
        new("Projects[].Local.RunOptions.AutoRestartOnWatchChanges", SettingsApplyImpact.HardRestart,
            "Watch rude-edit restart policy."),
        new("Projects[].Local.RunOptions.AutoRestartOnHotReloadRequest", SettingsApplyImpact.HardRestart,
            "Hot-reload restart policy."),
        new("Projects[].Local.RunOptions.RestartAppAfterRebuild", SettingsApplyImpact.HardRestart,
            "Post-build run policy."),
        new("Projects[].Local.RunOptions.RunTests", SettingsApplyImpact.HardRestart,
            "When tests auto-run relative to builds."),
        new("Projects[].Local.RunOptions.FileChanges", SettingsApplyImpact.HardRestart,
            "File-watcher rebuild trigger mode."),
        new("Projects[].Local.RunOptions.ReleaseOutputLocksBeforeBuild", SettingsApplyImpact.HardRestart,
            "Pre-build lock release behaviour."),
        new("Projects[].Local.RunOptions.ForceCompleteWarningCounts", SettingsApplyImpact.SoftRuntime,
            "Log/count presentation preference; UpdateDefinition can adopt without restart."),
        new("Projects[].Local.RunOptions.AutoRepairCorruptedOutput", SettingsApplyImpact.HardRestart,
            "Build repair behaviour for the live build path."),
        new("Projects[].Local.RunOptions.WatchExcludeSegments", SettingsApplyImpact.HardRestart,
            "Changes which paths the live file watcher ignores."),
        new("Projects[].Local.RunOptions.AutoOpenLog", SettingsApplyImpact.SoftRuntime,
            "UI auto-open preference; App reads settings without restarting Local."),
        new("Projects[].Local.RunOptions.ShowStatusPanelWhileBuilding", SettingsApplyImpact.SoftRuntime,
            "Status panel visibility preference only."),

        // --- Azure attachment ---
        new("Projects[].Azure.ConnectionId", SettingsApplyImpact.SoftRuntime,
            "Azure poller association."),
        new("Projects[].Azure.AdoProjectId", SettingsApplyImpact.SoftRuntime,
            "Azure project identity for polling."),
        new("Projects[].Azure.AdoProjectName", SettingsApplyImpact.SoftRuntime,
            "Azure display metadata."),
        new("Projects[].Azure.RepositoryId", SettingsApplyImpact.SoftRuntime,
            "Azure repo identity for polling."),
        new("Projects[].Azure.RepositoryName", SettingsApplyImpact.SoftRuntime,
            "Azure display metadata."),
        new("Projects[].Azure.RepositoryRemoteUrl", SettingsApplyImpact.SoftRuntime,
            "Azure remote metadata."),
        new("Projects[].Azure.DefaultBranch", SettingsApplyImpact.SoftRuntime,
            "Azure focus metadata; not Local."),
        new("Projects[].Azure.ExtraWatchedBranches[]", SettingsApplyImpact.SoftRuntime,
            "Azure attention branches."),
        new("Projects[].Azure.Pipelines[].DefinitionId", SettingsApplyImpact.SoftRuntime,
            "Which pipelines are polled."),
        new("Projects[].Azure.Pipelines[].DisplayName", SettingsApplyImpact.SoftRuntime,
            "Pipeline label."),
        new("Projects[].Azure.Pipelines[].IncludedBranches[]", SettingsApplyImpact.SoftRuntime,
            "Pipeline branch filter for Azure."),
        new("Projects[].Azure.Pipelines[].NotificationMode", SettingsApplyImpact.SoftRuntime,
            "Azure notification preference (deferred); no Local rebuild."),
        new("Projects[].Azure.Pipelines[].Priority", SettingsApplyImpact.SoftRuntime,
            "Azure pipeline ordering."),

        // --- monitor ---
        new("Monitor.HealthRefreshSeconds", SettingsApplyImpact.SoftRuntime,
            "Health coalesce cadence."),
        new("Monitor.FileChangeDebounceMs", SettingsApplyImpact.SoftRuntime,
            "UpdateDefinition applies debounce to the live watcher."),
        new("Monitor.FileChangeDebounceMode", SettingsApplyImpact.SoftRuntime,
            "Auto/manual debounce mode via UpdateDefinition."),
        new("Monitor.CoalesceWatchRebuilds", SettingsApplyImpact.SoftRuntime,
            "Watch coalesce flag via UpdateDefinition."),
        new("Monitor.MaxConcurrentActiveProjects", SettingsApplyImpact.SoftRuntime,
            "Caps concurrent starts; does not require stopping healthy runtimes."),
        new("Monitor.AutoOpenLogOnFailure", SettingsApplyImpact.SoftRuntime,
            "Obsolete migrated field; retained for load compatibility."),
        new("Monitor.AutoOpenBuildMonitorHealthOnStartup", SettingsApplyImpact.SoftRuntime,
            "Startup UI preference for next launch; no live rebuild."),
        new("Monitor.PlaySoundOnBuildError", SettingsApplyImpact.SoftRuntime,
            "Toast sound preference (Monitor-hosted); Soft avoids Local restart."),
        new("Monitor.PlaySoundOnBuildSuccess", SettingsApplyImpact.SoftRuntime,
            "Toast sound preference; Soft avoids Local restart."),
        new("Monitor.MaxLogDisplayBytes", SettingsApplyImpact.SoftRuntime,
            "Log viewer display cap."),
        new("Monitor.DeferStartupBuildUntilQuiet", SettingsApplyImpact.SoftRuntime,
            "Suppression settings applied on UpdateDefinition / next start."),
        new("Monitor.CancelSupersededBuilds", SettingsApplyImpact.SoftRuntime,
            "Build cancellation policy via UpdateDefinition."),
        new("Monitor.UseAgentTranscriptActivity", SettingsApplyImpact.SoftRuntime,
            "Edit-gating input; UpdateDefinition / session path."),
        new("Monitor.LearnFromDiagnosticsVerdicts", SettingsApplyImpact.SoftRuntime,
            "Learning flag via UpdateDefinition."),
        new("Monitor.ControlPlaneEnabled", SettingsApplyImpact.SoftRuntime,
            "Control-plane host enable; ApplyControlPlaneHost."),
        new("Monitor.ControlPlanePort", SettingsApplyImpact.SoftRuntime,
            "Control-plane bind port."),
        new("Monitor.ControlPlaneBusyTimeoutSeconds", SettingsApplyImpact.SoftRuntime,
            "Session busy timeout defaults."),
        new("Monitor.SuppressAutoBuildTests", SettingsApplyImpact.SoftRuntime,
            "Test suppression default for sessions."),

        // --- app behaviour (presentation) ---
        new("AppBehavior.RunOnLogon", SettingsApplyImpact.Presentation,
            "Windows startup registration only."),
        new("AppBehavior.StartMinimizedToTray", SettingsApplyImpact.Presentation,
            "Launch UI preference."),
        new("AppBehavior.Theme", SettingsApplyImpact.Presentation,
            "WPF theme."),
        new("AppBehavior.ToastPosition", SettingsApplyImpact.Presentation,
            "Toast placement."),
        new("AppBehavior.ToastDurationSeconds", SettingsApplyImpact.Presentation,
            "Toast duration."),
        new("AppBehavior.TrayMenuLayout", SettingsApplyImpact.Presentation,
            "Tray context-menu layout."),
        new("AppBehavior.FollowStatusPanelToVirtualDesktop", SettingsApplyImpact.Presentation,
            "Status panel VD follow."),
        new("AppBehavior.FollowBuildLogToVirtualDesktop", SettingsApplyImpact.Presentation,
            "Log viewer VD follow."),
        new("AppBehavior.Toasts.BuildStart", SettingsApplyImpact.Presentation,
            "Toast category toggle."),
        new("AppBehavior.Toasts.BuildSuccess", SettingsApplyImpact.Presentation,
            "Toast category toggle."),
        new("AppBehavior.Toasts.BuildFailure", SettingsApplyImpact.Presentation,
            "Toast category toggle."),
        new("AppBehavior.Toasts.FileChangeDetected", SettingsApplyImpact.Presentation,
            "Toast category toggle."),
        new("AppBehavior.Toasts.Warnings", SettingsApplyImpact.Presentation,
            "Toast category toggle."),
        new("AppBehavior.Toasts.Errors", SettingsApplyImpact.Presentation,
            "Toast category toggle."),
        new("AppBehavior.Toasts.Info", SettingsApplyImpact.Presentation,
            "Toast category toggle.")
    ];

    public static IReadOnlySet<string> Paths { get; } =
        All.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
}
