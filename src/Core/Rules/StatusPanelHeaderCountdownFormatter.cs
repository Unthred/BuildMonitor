using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Single header countdown for the hover status panel. Closing and rebuild timers are mutually exclusive.
/// </summary>
public static class StatusPanelHeaderCountdownFormatter
{
    public static string Format(
        IReadOnlyList<ProjectHealthSnapshot> snapshots,
        DateTimeOffset? panelDismissAtUtc,
        DateTimeOffset utcNow)
    {
        var closing = EditGatingDetailFormatter.FormatPanelDismissCountdown(panelDismissAtUtc, utcNow);
        if (!string.IsNullOrWhiteSpace(closing))
        {
            return closing;
        }

        var rebuildUntil = snapshots
            .Where(s => s.IsActive && s.RebuildQuietUntilUtc is not null)
            .OrderByDescending(s => s.LastChangedUtc)
            .Select(s => s.RebuildQuietUntilUtc)
            .FirstOrDefault();

        return EditGatingDetailFormatter.FormatCountdownRemaining(rebuildUntil, utcNow);
    }
}
