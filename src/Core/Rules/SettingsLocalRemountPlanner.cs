using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// How a Local project should be remounted after a HardRestart Settings diff.
/// Never implies <c>BuildAsync</c> — Settings Save is not a build trigger.
/// </summary>
public enum LocalRemountKind
{
    /// <summary>No remount work for this id (should not appear in plans).</summary>
    None = 0,

    /// <summary>Project deactivated — orchestrator disposes the runtime.</summary>
    StopOnly = 1,

    /// <summary>Newly activated — mount watcher only; do not compile or start the app.</summary>
    MountFresh = 2,

    /// <summary>Watch ignore / root-adjacent watcher config — recreate watcher; leave process running.</summary>
    WatcherOnly = 3,

    /// <summary>Launch profile, extra args, or RunMode — remount watcher and refresh process without build.</summary>
    ProcessAndWatcher = 4,

    /// <summary>RootFolder / ProjectFile — remount against new source; do not auto-compile or start process.</summary>
    SourceIdentity = 5
}

/// <summary>Per-project remount instruction derived from a Settings HardRestart diff.</summary>
public sealed record LocalProjectRemountPlan(string ProjectId, LocalRemountKind Kind);

/// <summary>
/// Identifies which Local projects a HardRestart Settings save must remount, and how.
/// Used so unrelated projects are not bounced.
/// </summary>
public static class SettingsLocalRemountPlanner
{
    public static IReadOnlyList<LocalProjectRemountPlan> Plan(AppSettings before, AppSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeLocal = before.Projects
            .Where(p => p.Local is not null)
            .ToDictionary(p => p.Id, StringComparer.Ordinal);
        var afterLocal = after.Projects
            .Where(p => p.Local is not null)
            .ToDictionary(p => p.Id, StringComparer.Ordinal);

        var ids = beforeLocal.Keys
            .Union(afterLocal.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);

        var plans = new List<LocalProjectRemountPlan>();
        foreach (var id in ids)
        {
            beforeLocal.TryGetValue(id, out var b);
            afterLocal.TryGetValue(id, out var a);

            if (b is null && a is not null)
            {
                // New Local id (including Id rename target).
                if (a.IsActiveInSession)
                {
                    plans.Add(new LocalProjectRemountPlan(id, LocalRemountKind.MountFresh));
                }

                continue;
            }

            if (b is not null && a is null)
            {
                // Local id removed (including Id rename source).
                plans.Add(new LocalProjectRemountPlan(id, LocalRemountKind.StopOnly));
                continue;
            }

            if (b is null || a is null)
            {
                continue;
            }

            var wasActive = b.IsActiveInSession;
            var isActive = a.IsActiveInSession;
            if (wasActive && !isActive)
            {
                plans.Add(new LocalProjectRemountPlan(id, LocalRemountKind.StopOnly));
                continue;
            }

            if (!wasActive && isActive)
            {
                plans.Add(new LocalProjectRemountPlan(id, LocalRemountKind.MountFresh));
                continue;
            }

            if (!wasActive && !isActive)
            {
                // Inactive Local hard-field edits do not touch a live runtime.
                continue;
            }

            // Both active — classify field deltas.
            var bl = b.Local!;
            var al = a.Local!;
            var sourceChanged =
                !string.Equals(bl.RootFolder, al.RootFolder, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(bl.ProjectFile, al.ProjectFile, StringComparison.OrdinalIgnoreCase);
            if (sourceChanged)
            {
                plans.Add(new LocalProjectRemountPlan(id, LocalRemountKind.SourceIdentity));
                continue;
            }

            var processChanged =
                !string.Equals(bl.LaunchProfile, al.LaunchProfile, StringComparison.Ordinal)
                || !string.Equals(bl.ExtraDotNetArgs, al.ExtraDotNetArgs, StringComparison.Ordinal)
                || bl.RunOptions.RunMode != al.RunOptions.RunMode;
            if (processChanged)
            {
                plans.Add(new LocalProjectRemountPlan(id, LocalRemountKind.ProcessAndWatcher));
                continue;
            }

            var watcherChanged = !string.Equals(
                bl.RunOptions.WatchExcludeSegments ?? "",
                al.RunOptions.WatchExcludeSegments ?? "",
                StringComparison.Ordinal);
            if (watcherChanged)
            {
                plans.Add(new LocalProjectRemountPlan(id, LocalRemountKind.WatcherOnly));
            }
        }

        return plans;
    }
}
