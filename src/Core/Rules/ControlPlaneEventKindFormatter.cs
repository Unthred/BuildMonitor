using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class ControlPlaneEventKindFormatter
{
    public static string ToLabel(ControlPlaneEventKind kind) =>
        kind switch
        {
            ControlPlaneEventKind.Busy => "Busy",
            ControlPlaneEventKind.IdleAgent => "Idle (agent)",
            ControlPlaneEventKind.IdleTimeout => "Idle (timeout)",
            ControlPlaneEventKind.BuildBlocked => "Build blocked",
            ControlPlaneEventKind.Rebuild => "Rebuild",
            ControlPlaneEventKind.Tests => "Tests",
            ControlPlaneEventKind.ShipCheck => "Ship-check",
            ControlPlaneEventKind.RunStop => "App stop",
            ControlPlaneEventKind.WatchPause => "Watch pause",
            ControlPlaneEventKind.WatchResume => "Watch resume",
            ControlPlaneEventKind.ModeChanged => "Mode changed",
            _ => kind.ToString()
        };
}
