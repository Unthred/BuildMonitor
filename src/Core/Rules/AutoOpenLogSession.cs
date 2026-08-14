using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Remembers the last observed build result per project so auto-open fires once per
/// completed failed build, including watch rebuilds that stay <see cref="ProjectLifecycleState.Watching"/>.
/// </summary>
public sealed class AutoOpenLogSession
{
    private readonly Dictionary<string, ObservedBuild> previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> latched = new(StringComparer.OrdinalIgnoreCase);

    public void Reset()
    {
        previous.Clear();
        latched.Clear();
    }

    public bool ShouldOpenViewer(AutoOpenLogMode mode, ProjectHealthSnapshot snapshot)
    {
        if (mode == AutoOpenLogMode.Never)
        {
            return false;
        }

        var hadPrevious = previous.TryGetValue(snapshot.ProjectId, out var prior);
        if (hadPrevious && prior.LastBuildFinishedAtUtc != snapshot.LastBuildFinishedAtUtc)
        {
            latched.Remove(snapshot.ProjectId);
        }

        var shouldOpen = AutoOpenLogTransitionEvaluator.ShouldOpen(
            mode,
            hadPrevious ? prior.Health : MonitorHealth.Unknown,
            snapshot.Health,
            hadPrevious ? prior.State : ProjectLifecycleState.Idle,
            snapshot.State,
            snapshot.ErrorCount,
            hadPrevious,
            hadPrevious ? prior.LastBuildFinishedAtUtc : null,
            snapshot.LastBuildExitCode,
            snapshot.LastBuildFinishedAtUtc);

        var opened = false;
        var useLatch = mode is AutoOpenLogMode.Errors or AutoOpenLogMode.Warnings;
        if (shouldOpen)
        {
            opened = !useLatch || latched.Add(snapshot.ProjectId);
        }
        else if (AutoOpenLogTransitionEvaluator.ShouldResetOpenLatch(mode, snapshot.Health))
        {
            latched.Remove(snapshot.ProjectId);
        }

        previous[snapshot.ProjectId] = new ObservedBuild(
            snapshot.Health,
            snapshot.State,
            snapshot.LastBuildExitCode,
            snapshot.LastBuildFinishedAtUtc);
        return opened;
    }

    public void ForgetInactive(IReadOnlyCollection<string> activeProjectIds)
    {
        var active = activeProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in previous.Keys.Where(id => !active.Contains(id)).ToList())
        {
            previous.Remove(staleId);
            latched.Remove(staleId);
        }
    }

    private readonly record struct ObservedBuild(
        MonitorHealth Health,
        ProjectLifecycleState State,
        int LastBuildExitCode,
        DateTimeOffset? LastBuildFinishedAtUtc);
}
