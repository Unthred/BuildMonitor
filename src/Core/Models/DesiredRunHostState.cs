namespace BuildMonitor.Core.Models;

/// <summary>
/// Desired supervised run/watch host state. Distinct from temporary operational pause
/// (ship-check / rebuild / tests unlocking DLLs) and from actual process liveness.
/// </summary>
public enum DesiredRunHostState
{
    /// <summary>Host must stay stopped until an explicit Run/Restart or cold StartOnLaunch start.</summary>
    Stopped = 0,

    /// <summary>Host should be running; crash recovery and operation resume may restore it.</summary>
    Running = 1
}
