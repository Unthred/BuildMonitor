namespace BuildMonitor.Core.Models;

/// <summary>Why a file-triggered rebuild is waiting instead of building immediately.</summary>
public enum PendingRebuildHoldReason
{
    None = 0,
    /// <summary>Quiet period after the last save (agent session coalescing).</summary>
    EditsSettling = 1,
    /// <summary>New saves arrived — the wait timer was restarted.</summary>
    EditsStillArriving = 2,
    /// <summary>A build is already running.</summary>
    BuildInProgress = 3,
    /// <summary>Tests are running.</summary>
    TestsInProgress = 4,
    /// <summary>Post-build cooldown window.</summary>
    PostBuildCooldown = 5,
    /// <summary>Startup build waiting for edit quiet period.</summary>
    StartupDeferred = 6,
    /// <summary>In-flight build cancelled; waiting for edits to settle before rebuild.</summary>
    SupersededByNewEdits = 7
}
