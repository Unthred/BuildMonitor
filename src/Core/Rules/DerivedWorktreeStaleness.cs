using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Decides whether a control-plane derived worktree project is finished and can be
/// offered for unregister. Manual projects (no <c>derivedFromProjectId</c>) are never stale.
/// </summary>
public static class DerivedWorktreeStaleness
{
    public static bool IsDerived(MonitoredProjectSettings project) =>
        !string.IsNullOrWhiteSpace(project.Local?.DerivedFromProjectId);

    public static IReadOnlyList<StaleDerivedWorktree> Evaluate(
        IReadOnlyList<MonitoredProjectSettings> projects,
        Func<string, DerivedWorktreeFolderProbe> probeFolder)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(probeFolder);

        var projectIds = new HashSet<string>(
            projects.Select(p => p.Id),
            StringComparer.OrdinalIgnoreCase);
        var stale = new List<StaleDerivedWorktree>();
        foreach (var project in projects)
        {
            if (!IsDerived(project))
            {
                continue;
            }

            var local = project.Local!;
            var reason = Classify(local, projectIds, probeFolder(local.RootFolder));
            if (reason is { } r)
            {
                stale.Add(new StaleDerivedWorktree(project.Id, project.DisplayName, local.RootFolder, r));
            }
        }

        return stale;
    }

    public static string Describe(DerivedWorktreeStaleReason reason) => reason switch
    {
        DerivedWorktreeStaleReason.FolderMissing => "folder removed",
        DerivedWorktreeStaleReason.NotGitWorktree => "no longer a Git worktree",
        DerivedWorktreeStaleReason.ParentMissing => "parent project removed",
        _ => reason.ToString()
    };

    private static DerivedWorktreeStaleReason? Classify(
        LocalProjectAttachment local,
        HashSet<string> projectIds,
        DerivedWorktreeFolderProbe probe)
    {
        // Folder facts win over the orphan check: a removed folder is the clearer reason.
        if (string.IsNullOrWhiteSpace(local.RootFolder) || !probe.FolderExists)
        {
            return DerivedWorktreeStaleReason.FolderMissing;
        }

        if (!probe.HasLiveGitWorktree)
        {
            return DerivedWorktreeStaleReason.NotGitWorktree;
        }

        if (!projectIds.Contains(local.DerivedFromProjectId!))
        {
            return DerivedWorktreeStaleReason.ParentMissing;
        }

        return null;
    }
}
