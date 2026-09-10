namespace BuildMonitor.Core.Rules;

/// <summary>
/// In-memory cycle gate for #94 activity auto-show (#132).
/// Deliberate dismiss while a panel-wide Local/Azure hold is active suppresses further
/// activity-driven auto-show until that hold fully clears. Hover/manual open is unaffected.
/// </summary>
public sealed class StatusPanelActivityAutoShowCycle
{
    public bool IsSuppressed { get; private set; }

    /// <summary>
    /// Call on every visibility sync with the current panel-wide hold.
    /// Clears suppression when the continuous activity cycle settles.
    /// </summary>
    public void ObserveActivityHold(bool hasQualifyingActivityHold)
    {
        if (!hasQualifyingActivityHold)
        {
            IsSuppressed = false;
        }
    }

    /// <summary>
    /// Starts suppression only when the user deliberately closes the panel while a hold is active.
    /// Automatic hides must not call this.
    /// </summary>
    public void OnDeliberateDismiss(bool hasQualifyingActivityHold)
    {
        if (hasQualifyingActivityHold)
        {
            IsSuppressed = true;
        }
    }

    public bool ShouldAutoShowForActivityHold(bool hasQualifyingActivityHold) =>
        hasQualifyingActivityHold && !IsSuppressed;
}
