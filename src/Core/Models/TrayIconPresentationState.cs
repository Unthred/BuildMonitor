namespace BuildMonitor.Core.Models;

/// <summary>Tray notify-icon presentation states for the builder-duck asset family (#95).</summary>
public enum TrayIconPresentationState
{
    Neutral = 0,
    Healthy = 1,
    Building = 2,
    Attention = 3,
    Failed = 4
}
