using System.Text.Json;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// How aggressively Settings Save must touch Local project runtimes.
/// Saving Settings is never itself a reason to build — only Local-affecting diffs are.
/// </summary>
public enum SettingsApplyImpact
{
    /// <summary>No meaningful difference (or identity-only fields such as schema version).</summary>
    None = 0,

    /// <summary>
    /// AppBehavior / presentation-only (tray layout, theme, toasts, virtual-desktop follow, etc.).
    /// Persist + refresh UI; do not stop/start Local runtimes.
    /// </summary>
    Presentation = 1,

    /// <summary>
    /// Monitor, connections, Azure, display names, Local policy/UI prefs (tests, restart flags,
    /// build-control mode, etc.) — refresh orchestrator/Azure without StopAll + StartActive.
    /// </summary>
    SoftRuntime = 2,

    /// <summary>
    /// Local process/watcher identity (paths, launch/args, RunMode, watch excludes) or Local
    /// active-session membership — stop/restart Local runtimes (may build via StartAsync when
    /// StartOnLaunch is enabled). There is no restart-without-build apply path yet.
    /// </summary>
    HardRestart = 3
}

/// <summary>Actions derived from <see cref="SettingsApplyImpact"/> for the tray apply path.</summary>
public sealed record SettingsApplyPlan(
    SettingsApplyImpact Impact,
    bool StopAllAndRestartActiveProjects,
    bool ApplyOrchestratorSettings,
    bool ResetHealthTransitionState,
    bool ShowProjectsStartingToast);

/// <summary>
/// Classifies Settings Save diffs so presentation/Azure/monitor updates are not treated as launch.
/// Field groupings follow <see cref="SettingsApplyImpactCatalog"/>.
/// </summary>
public static class SettingsApplyImpactClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static SettingsApplyImpact Classify(AppSettings? before, AppSettings after)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (before is null)
        {
            return SettingsApplyImpact.HardRestart;
        }

        var beforeLocal = SerializeLocalHardFingerprint(before);
        var afterLocal = SerializeLocalHardFingerprint(after);
        if (!string.Equals(beforeLocal, afterLocal, StringComparison.Ordinal))
        {
            return SettingsApplyImpact.HardRestart;
        }

        var beforeSoft = SerializeSoftRuntimeFingerprint(before);
        var afterSoft = SerializeSoftRuntimeFingerprint(after);
        if (!string.Equals(beforeSoft, afterSoft, StringComparison.Ordinal))
        {
            return SettingsApplyImpact.SoftRuntime;
        }

        var beforeUi = Serialize(before.AppBehavior);
        var afterUi = Serialize(after.AppBehavior);
        if (!string.Equals(beforeUi, afterUi, StringComparison.Ordinal))
        {
            return SettingsApplyImpact.Presentation;
        }

        return SettingsApplyImpact.None;
    }

    public static SettingsApplyPlan CreatePlan(AppSettings? before, AppSettings after)
    {
        var impact = Classify(before, after);
        return impact switch
        {
            SettingsApplyImpact.None => new SettingsApplyPlan(
                Impact: impact,
                StopAllAndRestartActiveProjects: false,
                ApplyOrchestratorSettings: false,
                ResetHealthTransitionState: false,
                ShowProjectsStartingToast: false),
            SettingsApplyImpact.Presentation => new SettingsApplyPlan(
                Impact: impact,
                StopAllAndRestartActiveProjects: false,
                ApplyOrchestratorSettings: false,
                ResetHealthTransitionState: false,
                ShowProjectsStartingToast: false),
            SettingsApplyImpact.SoftRuntime => new SettingsApplyPlan(
                Impact: impact,
                StopAllAndRestartActiveProjects: false,
                ApplyOrchestratorSettings: true,
                ResetHealthTransitionState: false,
                ShowProjectsStartingToast: false),
            _ => new SettingsApplyPlan(
                Impact: SettingsApplyImpact.HardRestart,
                StopAllAndRestartActiveProjects: true,
                ApplyOrchestratorSettings: true,
                ResetHealthTransitionState: true,
                ShowProjectsStartingToast: true)
        };
    }

    /// <summary>
    /// Local projects only: active flag + build/run-defining Local fields.
    /// Azure-only projects are excluded so attaching Azure does not HardRestart.
    /// </summary>
    private static string SerializeLocalHardFingerprint(AppSettings settings)
    {
        var rows = settings.Projects
            .Where(p => p.Local is not null)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => new
            {
                p.Id,
                p.IsActiveInSession,
                Local = SliceLocalHard(p.Local!)
            });
        return Serialize(rows);
    }

    /// <summary>
    /// Monitor, connections, Azure, display names, Azure-only active flag, Local UI preferences.
    /// </summary>
    private static string SerializeSoftRuntimeFingerprint(AppSettings settings)
    {
        var rows = settings.Projects
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => new
            {
                p.Id,
                p.DisplayName,
                AzureOnlyActive = p.Local is null ? p.IsActiveInSession : (bool?)null,
                LocalUi = p.Local is null ? null : SliceLocalSoft(p.Local),
                p.Azure
            });
        return Serialize(new
        {
            settings.Monitor,
            settings.Connections,
            Projects = rows
        });
    }

    private static object SliceLocalHard(LocalProjectAttachment local) => new
    {
        local.RootFolder,
        local.ProjectFile,
        local.LaunchProfile,
        local.ExtraDotNetArgs,
        RunOptions = new
        {
            local.RunOptions.RunMode,
            local.RunOptions.WatchExcludeSegments
        }
    };

    private static object SliceLocalSoft(LocalProjectAttachment local) => new
    {
        local.TestProjectFile,
        local.StartOnLaunch,
        local.BuildControlMode,
        local.PreferredSiteUrlScheme,
        RunOptions = new
        {
            local.RunOptions.RestartOnCrash,
            local.RunOptions.MaxRestartRetries,
            local.RunOptions.AutoRestartOnWatchChanges,
            local.RunOptions.AutoRestartOnHotReloadRequest,
            local.RunOptions.RestartAppAfterRebuild,
            local.RunOptions.RunTests,
            local.RunOptions.FileChanges,
            local.RunOptions.ReleaseOutputLocksBeforeBuild,
            local.RunOptions.AutoRepairCorruptedOutput,
            local.RunOptions.AutoOpenLog,
            local.RunOptions.ShowStatusPanelWhileBuilding,
            local.RunOptions.ForceCompleteWarningCounts
        }
    };

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);
}
