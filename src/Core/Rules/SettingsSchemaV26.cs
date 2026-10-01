using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Schema v26 flips derived worktree <c>startOnLaunch</c> to true (see ADR 0004).
/// Existing projects with <c>derivedFromProjectId</c> are migrated on load.
/// </summary>
public static class SettingsSchemaV26
{
    public const int Version = 26;

    public static void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var project in settings.Projects)
        {
            if (project.Local is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(project.Local.DerivedFromProjectId))
            {
                project.Local.StartOnLaunch = true;
            }
        }
    }
}
