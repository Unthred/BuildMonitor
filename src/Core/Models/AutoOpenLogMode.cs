namespace BuildMonitor.Core.Models;

/// <summary>When to open the log viewer automatically for this project.</summary>
public enum AutoOpenLogMode
{
    Never = 0,
    Errors = 1,
    Warnings = 2,
    Always = 3
}
