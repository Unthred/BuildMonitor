using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Git;

namespace BuildMonitor.Infrastructure.Services;

public sealed partial class ProjectOrchestrator
{
    private static readonly TimeSpan StaleScanFirstDelay = TimeSpan.FromSeconds(30);

    private readonly HashSet<string> notifiedStaleWorktreeIds = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, DerivedWorktreeFolderProbe> derivedWorktreeFolderProbe = DerivedWorktreeFolderProber.Probe;
    private Timer? staleWorktreeScanTimer;

    /// <summary>Test seam for the file-system probe used by stale derived worktree detection.</summary>
    internal void SetDerivedWorktreeFolderProbe(Func<string, DerivedWorktreeFolderProbe> probe) =>
        derivedWorktreeFolderProbe = probe ?? throw new ArgumentNullException(nameof(probe));

    /// <summary>
    /// Derived worktree projects whose folder is gone, is no longer a Git worktree, or whose
    /// parent project no longer exists. Manual projects are never returned.
    /// </summary>
    public IReadOnlyList<StaleDerivedWorktree> GetStaleDerivedWorktrees()
    {
        List<MonitoredProjectSettings> projects;
        lock (sync)
        {
            projects = settings.Projects.ToList();
        }

        return DerivedWorktreeStaleness.Evaluate(projects, derivedWorktreeFolderProbe);
    }

    /// <summary>Scan now and every <paramref name="interval"/>; raises one notification per newly stale set.</summary>
    public void StartStaleDerivedWorktreeScan(TimeSpan interval)
    {
        staleWorktreeScanTimer?.Dispose();
        staleWorktreeScanTimer = new Timer(
            _ => ScanStaleDerivedWorktrees(),
            null,
            StaleScanFirstDelay,
            interval);
    }

    /// <summary>
    /// Evaluates staleness and notifies once for projects not reported before. Never unregisters.
    /// </summary>
    public IReadOnlyList<StaleDerivedWorktree> ScanStaleDerivedWorktrees()
    {
        IReadOnlyList<StaleDerivedWorktree> stale;
        try
        {
            stale = GetStaleDerivedWorktrees();
        }
        catch (Exception ex)
        {
            // Background scan must never crash the timer thread; the next tick retries.
            System.Diagnostics.Debug.WriteLine($"Stale derived worktree scan failed: {ex}");
            return [];
        }

        List<StaleDerivedWorktree> fresh;
        lock (notifiedStaleWorktreeIds)
        {
            notifiedStaleWorktreeIds.IntersectWith(stale.Select(s => s.ProjectId));
            fresh = stale.Where(s => notifiedStaleWorktreeIds.Add(s.ProjectId)).ToList();
        }

        if (fresh.Count > 0)
        {
            RaiseUserNotification(
                string.Empty,
                fresh.Count == 1 ? "Finished worktree" : $"{fresh.Count} finished worktrees",
                string.Join(
                    Environment.NewLine,
                    fresh.Select(s => $"{s.DisplayName} — {DerivedWorktreeStaleness.Describe(s.Reason)}"))
                + Environment.NewLine
                + "Unregister from the tray menu: Finished worktrees.",
                UserNotificationKind.Info,
                UserNotificationCategory.Info);
        }

        return stale;
    }

    /// <summary>
    /// Unregisters the given projects through the derived unregister path, but only those
    /// still stale now. The worktree folder is never touched.
    /// </summary>
    public async Task<IReadOnlyList<StaleDerivedWorktreeUnregisterOutcome>> UnregisterStaleDerivedWorktreesAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        var requested = new HashSet<string>(projectIds, StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<StaleDerivedWorktreeUnregisterOutcome>();
        foreach (var worktree in GetStaleDerivedWorktrees().Where(s => requested.Contains(s.ProjectId)))
        {
            var result = await UnregisterDerivedWorktreeAsync(
                    new ControlPlaneUnregisterWorktreeRequest(worktree.ProjectId, worktree.RootFolder),
                    cancellationToken)
                .ConfigureAwait(false);
            outcomes.Add(new StaleDerivedWorktreeUnregisterOutcome(worktree, result.Ok, result.Error));
        }

        return outcomes;
    }
}
