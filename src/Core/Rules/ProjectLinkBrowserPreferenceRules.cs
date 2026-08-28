using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>Reads per-project link browser preference (null/empty = system default).</summary>
public static class ProjectLinkBrowserPreferenceRules
{
    public static string? ResolveRegisteredBrowserId(MonitoredProjectSettings? project)
    {
        var id = project?.LinkBrowserRegisteredId;
        return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
    }

    public static string? ResolveRegisteredBrowserId(AppSettings settings, string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var project = settings.Projects.FirstOrDefault(
            p => string.Equals(p.Id, projectId.Trim(), StringComparison.OrdinalIgnoreCase));
        return ResolveRegisteredBrowserId(project);
    }
}
