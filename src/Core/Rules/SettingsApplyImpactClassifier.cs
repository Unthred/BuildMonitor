using System.Text.Json;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// How aggressively Settings Save must touch Local project runtimes.
/// Saving Settings is never itself a reason to build — only Local-affecting diffs are.
/// </summary>
public enum SettingsApplyImpact
{
    /// <summary>No meaningful difference.</summary>
    None = 0,

    /// <summary>
    /// AppBehavior / presentation-only (tray layout, theme, toasts, virtual-desktop follow, etc.).
    /// Persist + refresh UI; do not stop/start Local runtimes.
    /// </summary>
    Presentation = 1,

    /// <summary>
    /// Monitor, connections, Azure attachments, or display names — refresh orchestrator/Azure
    /// without StopAll + StartActive (no Local rebuild side effect).
    /// </summary>
    SoftRuntime = 2,

    /// <summary>
    /// Local attachment / active-session membership changed — stop/restart Local runtimes
    /// (may build via StartAsync when StartOnLaunch is enabled).
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

        var beforeLocal = SerializeLocalRuntimeFingerprint(before);
        var afterLocal = SerializeLocalRuntimeFingerprint(after);
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
    /// Local build/run identity: active flag + Local attachment. Azure/display name excluded.
    /// </summary>
    private static string SerializeLocalRuntimeFingerprint(AppSettings settings)
    {
        var rows = settings.Projects
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => new
            {
                p.Id,
                p.IsActiveInSession,
                Local = p.Local
            });
        return Serialize(rows);
    }

    /// <summary>
    /// Monitor, org connections, per-project Azure + display name (no Local rebuild required).
    /// </summary>
    private static string SerializeSoftRuntimeFingerprint(AppSettings settings)
    {
        var rows = settings.Projects
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => new
            {
                p.Id,
                p.DisplayName,
                p.Azure
            });
        return Serialize(new
        {
            settings.Monitor,
            settings.Connections,
            Projects = rows
        });
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);
}
