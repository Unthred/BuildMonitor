namespace BuildMonitor.Core.Rules;

using BuildMonitor.Core.Models;

/// <summary>Which projects the status panel lists.</summary>
public enum StatusPanelScope
{
    /// <summary>Every active project (tray hover or click).</summary>
    AllProjects,

    /// <summary>Only projects that are, or have been, busy since the panel opened by itself.</summary>
    BusyProjects
}

/// <summary>
/// Narrows the status panel to busy projects when it opens by itself, so one build does not
/// bring up a card for every registered worktree. A project that was busy at any point while
/// the panel is up keeps its card until the panel closes, so a finished build still shows its
/// result. Hovering or clicking the tray icon switches back to every project.
/// </summary>
public sealed class StatusPanelFocus
{
    private readonly HashSet<string> focusedProjectIds = new(StringComparer.Ordinal);

    public StatusPanelScope Scope { get; private set; } = StatusPanelScope.AllProjects;

    /// <summary>Switch to every project. Returns true when the scope changed.</summary>
    public bool ShowAllProjects()
    {
        focusedProjectIds.Clear();
        if (Scope == StatusPanelScope.AllProjects)
        {
            return false;
        }

        Scope = StatusPanelScope.AllProjects;
        return true;
    }

    /// <summary>Switch to busy projects, starting a fresh set. Returns true when the scope changed.</summary>
    public bool ShowBusyProjects()
    {
        if (Scope == StatusPanelScope.BusyProjects)
        {
            return false;
        }

        focusedProjectIds.Clear();
        Scope = StatusPanelScope.BusyProjects;
        return true;
    }

    /// <summary>
    /// Returns the presentation with cards limited to the focused projects. Records any project
    /// that is busy now. When none has been busy yet, every card is kept rather than an empty panel.
    /// </summary>
    public StatusPanelPresentation Apply(
        StatusPanelPresentation presentation,
        IEnumerable<ProjectHealthSnapshot> snapshots,
        bool includeAzureActivity)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(snapshots);

        if (Scope == StatusPanelScope.AllProjects)
        {
            return presentation;
        }

        foreach (var snapshot in snapshots)
        {
            if (IsBusy(snapshot, includeAzureActivity))
            {
                focusedProjectIds.Add(snapshot.ProjectId);
            }
        }

        var cards = presentation.Cards
            .Where(card => focusedProjectIds.Contains(card.ProjectId))
            .ToList();
        return cards.Count == 0
            ? presentation
            : presentation with { Cards = cards };
    }

    /// <summary>
    /// Busy: building, testing, waiting for edits, edit gating, restarting, Azure CI activity
    /// (when included), or a failed build, test or run that needs attention.
    /// </summary>
    public static bool IsBusy(ProjectHealthSnapshot snapshot, bool includeAzureActivity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsActive)
        {
            return false;
        }

        return StatusPanelVisibilityPolicy.IsQualifyingLocalBuildActivity(snapshot)
            || StatusPanelBuildVisibilityEvaluator.IsBusyWorkState(snapshot.State)
            || snapshot.IsEditGatingActive
            || snapshot.State is ProjectLifecycleState.BuildFailed
                or ProjectLifecycleState.TestFailed
                or ProjectLifecycleState.Crashed
            || (includeAzureActivity && StatusPanelVisibilityPolicy.IsQualifyingAzureBuildActivity(snapshot));
    }

    /// <summary>Header text: "Build status (N of M)" when filtered, otherwise the usual count.</summary>
    public static string FormatHeader(int shownCount, int activeCount, StatusPanelScope scope)
    {
        if (scope == StatusPanelScope.BusyProjects && shownCount < activeCount)
        {
            return $"Build status ({shownCount} of {activeCount})";
        }

        return activeCount > 1 ? $"Build status ({activeCount})" : "Build status";
    }
}
