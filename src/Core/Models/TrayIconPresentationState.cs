namespace BuildMonitor.Core.Models;

/// <summary>Tray health-ring colour (#129). Orthogonal to activity animation.</summary>
public enum TrayHealthRing
{
    Neutral = 0,
    Healthy = 1,
    Attention = 2,
    Failed = 3
}

/// <summary>
/// Tray notify-icon presentation: ring colour + whether activity animation may run.
/// Failed always renders static red even when <see cref="IsActive"/> is true.
/// </summary>
public readonly record struct TrayIconPresentation(TrayHealthRing Health, bool IsActive)
{
    public bool IsAnimatable => IsActive && Health != TrayHealthRing.Failed;
}
